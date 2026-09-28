using System.Windows;
using AnimatedWallPaper.Services;

namespace AnimatedWallPaper;

public partial class MainWindow
{
    private bool _oceanOpen;
    private readonly Dictionary<string,OceanDraft> _oceanDrafts = new(StringComparer.OrdinalIgnoreCase);

    private OceanPreferences OceanPreferencesFor(WallpaperEntry entry,string? deviceId)
        => (deviceId is null ? null : _settings.DisplayWallpapers.GetValueOrDefault(deviceId)?.Oceans.GetValueOrDefault(entry.Id))?.Normalize()
            ?? new OceanPreferences().Normalize();

    private OceanDraft OceanDraftFor(string deviceId)
    {
        if(_oceanDrafts.TryGetValue(deviceId,out var draft)) return draft;
        var entry=WallpaperGallery.Items.Cast<WallpaperEntry>().First(e=>e.Kind==WallpaperKind.Ocean);
        var active=_wallpaperController.ActiveRequests.GetValueOrDefault(deviceId);
        var runtime=active?.Kind==WallpaperKind.Ocean?active.Ocean:null;
        var preferences=runtime?.Frame().Snapshot.Preferences??OceanPreferencesFor(entry,deviceId);
        return _oceanDrafts[deviceId]=new(preferences,runtime);
    }

    private void InitializeOceanEditor()
    {
        OceanEditor.BackRequested+=()=>{_oceanOpen=false;UpdateLayoutMode();};
        OceanEditor.GeneralSettingsRequested+=()=>{_appSettingsOpen=true;UpdateLayoutMode();};
        OceanEditor.DisplayRequested+=id=>
        {
            var display=TargetDisplayCombo.Items.Cast<PreviewDisplay>().FirstOrDefault(d=>d.Target.DeviceId==id);
            if(display is null)return;
            _syncingDisplaySelection=true;
            TargetDisplayCombo.SelectedItem=PreviewDisplayCombo.SelectedItem=display;
            _syncingDisplaySelection=false;
            BindOceanEditor();UpdateStatus();
        };
        OceanEditor.ApplyRequested+=async all=>await ApplyOceanAsync(all);
    }

    internal void OpenOceanEditor()
    {
        if(!_license.CanPlay||IsPreviewSimulation)return;
        _settingsWindow?.Close();_appSettingsOpen=false;_oceanOpen=true;
        BindOceanEditor();UpdateLayoutMode();
    }

    private void BindOceanEditor()
    {
        if(!_oceanOpen)return;
        var displays=TargetDisplayCombo.Items.Cast<PreviewDisplay>().Select(d=>new OceanEditorControl.DisplayChoice(
            d.Target.DeviceId,OceanText.Format("Display {0}",d.Number),d.Aspect)).ToArray();
        if(SelectedDisplay is { } selected)
            OceanEditor.SetDraft(selected.Target.DeviceId,OceanDraftFor(selected.Target.DeviceId),displays,_settings.FramesPerSecond);
        OceanEditor.SetApplyState(_license.CanPlay&&SelectedDisplay is not null&&!_recovering,_changingWallpaper);
    }

    private async Task ApplyOceanAsync(bool all)
    {
        if(_isQuitting||IsPreviewSimulation||!_license.CanPlay||_changingWallpaper||_recovering||SelectedDisplay is not { } display)return;
        var draft=OceanDraftFor(display.Target.DeviceId);
        var submitted=draft.Preferences;
        var snapshot=draft.Runtime.Fork();
        var targets=all?_getMonitorTargets():_getMonitorTargets().Where(t=>t.DeviceId==display.Target.DeviceId).ToArray();
        if(targets.Length==0){OceanEditor.SetResult(OceanText.T("Connect a display to apply."));return;}
        _changingWallpaper=true;_playRequested=true;var revision=++_selectionRevision;
        _recoveryTimer.Stop();_recoveryAttempts=0;ErrorText.Text="";UpdatePreviewSuspension();UpdateStatus();
        AppLog.Write($"Ocean apply requested. Display={display.Target.DeviceId}; All={all}; Revision={revision}");
        var errors=new List<string>();var any=false;
        try
        {
            foreach(var target in targets)
            {
                if(_isQuitting||!_license.CanPlay||revision!=_selectionRevision)break;
                try
                {
                    var runtime=snapshot.Fork();
                    await _wallpaperController.StartAsync(new("ocean",WallpaperKind.Ocean,_settings.FramesPerSecond,
                        OceanPreferences:submitted,Ocean:runtime),target);
                    AppLog.Write($"Ocean apply ready. Display={target.DeviceId}; Revision={revision}");
                    if(!_license.CanPlay){StopWallpapers(persist:false);break;}
                    if(_isQuitting||revision!=_selectionRevision)break;
                    var saved=DisplayPreferences(target.DeviceId);saved.Enabled=true;saved.WallpaperId="ocean";saved.Oceans["ocean"]=runtime.Checkpoint();
                    _monitorPreviewDrafts[target.DeviceId]="ocean";
                    if(_oceanDrafts.TryGetValue(target.DeviceId,out var targetDraft))targetDraft.MarkApplied(submitted,snapshot);
                    any=true;QueueSave();
                }
                catch(OperationCanceledException) { }
                catch(Exception exception){AppLog.WriteException("Apply ocean",exception);errors.Add(target.DeviceName+": "+exception.Message);}
            }
        }
        finally
        {
            if(revision==_selectionRevision)
            {
                _changingWallpaper=false;_playRequested=_wallpaperController.DesiredRequests.Count>0;
                _foregroundMonitor.RefreshNow();ApplyPlaybackPolicy();UpdateStatus();
                if(errors.Count>0)ErrorText.Text=string.Join("\n",errors);
                if(_layoutRecoveryPending)ScheduleRecovery();
                OceanEditor.SetResult(errors.Count>0
                    ? OceanText.T(any?"Some displays could not be updated. Successful displays kept the new ocean.":"Ocean could not be applied. Your previous wallpaper was kept.")+"\n"+string.Join("\n",errors)
                    : any?OceanText.T("Ocean applied."):OceanText.T("Preview only · your desktop changes when you apply."));
            }
        }
    }

    private void CheckpointOceans()
    {
        foreach(var (id,request) in _wallpaperController.ActiveRequests)
            if(request.Ocean is { } runtime)
                DisplayPreferences(id).Oceans[request.Id]=runtime.Checkpoint();
    }
}
