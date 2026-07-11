#include <windows.h>
#include <d3d11.h>
#include <algorithm>
#include <array>
#include <cmath>
#include <string>
#include <Effekseer.h>
#include <EffekseerRendererDX11.h>

#ifdef HYPNIX_EFFEKSEER_EXPORTS
#define HYPNIX_API extern "C" __declspec(dllexport)
#else
#define HYPNIX_API extern "C" __declspec(dllimport)
#endif

namespace
{
struct BridgeState
{
    static constexpr int FlameCount = 48;
    static constexpr int SmokeCount = 12;
    Effekseer::ManagerRef manager;
    EffekseerRendererDX11::RendererRef renderer;
    Effekseer::EffectRef fireEffect;
    Effekseer::EffectRef smokeEffect;
    std::array<Effekseer::Handle, FlameCount> flameHandles{};
    std::array<float, FlameCount> flameAges{};
    std::array<float, FlameCount> flameLifetimes{};
    std::array<Effekseer::Handle, SmokeCount> smokeHandles{};
    float elapsed = 0.0f;
    bool volumetricJet = false;
    int activeFlameCount = FlameCount;
    float previousJetEnergy = 0.0f;
    float jetEmissionCooldown = 0.0f;
    int nextJetIndex = 0;

    BridgeState()
    {
        flameHandles.fill(-1);
        flameAges.fill(0.0f);
        flameLifetimes.fill(0.0f);
        smokeHandles.fill(-1);
    }
};

void StartVolumetricJet(BridgeState& state, float energy)
{
    const int index = state.nextJetIndex++ % state.activeFlameCount;
    auto& handle = state.flameHandles[index];
    if (handle >= 0 && state.manager->Exists(handle)) state.manager->StopEffect(handle);
    handle = state.manager->Play(state.fireEffect, Effekseer::Vector3D(0.0f, 0.0f, 0.0f), 0);
    state.flameAges[index] = 0.0f;
    state.flameLifetimes[index] = 0.58f + energy * 0.50f + (index % 3) * 0.07f;
}

void UpdateVolumetricJet(BridgeState& state, int index, float delta, float bass, float mids, float highs, float intensity)
{
    constexpr float pi = 3.14159265358979323846f;
    auto& handle = state.flameHandles[index];
    if (handle < 0 || !state.manager->Exists(handle)) return;

    state.flameAges[index] += delta;
    const float lifetime = std::max(0.01f, state.flameLifetimes[index]);
    const float progress = state.flameAges[index] / lifetime;
    if (progress >= 1.0f)
    {
        state.manager->StopEffect(handle);
        handle = -1;
        return;
    }

    const float attack = std::clamp(progress / 0.16f, 0.0f, 1.0f);
    const float release = std::clamp((1.0f - progress) / 0.34f, 0.0f, 1.0f);
    const float envelope = attack * attack * release;
    const float drive = std::clamp(0.70f + intensity * 0.75f, 0.70f, 2.60f);
    const float energy = std::clamp((bass * 0.62f + mids * 0.25f + highs * 0.13f) * drive, 0.0f, 1.0f);
    const float angle = (static_cast<float>(index) / state.activeFlameCount) * pi * 2.0f +
                        std::sin(index * 7.13f) * 0.10f;
    const float radius = 3.48f + progress * (0.10f + energy * 0.18f);
    const float body = envelope * (0.48f + energy * 0.34f);
    const float length = body * (1.08f + bass * 0.22f);

    state.manager->SetLocation(handle, std::cos(angle) * radius, std::sin(angle) * radius, 0.0f);
    state.manager->SetRotation(handle, 0.0f, 0.0f, angle - pi * 0.5f);
    state.manager->SetScale(handle, std::max(0.001f, body), std::max(0.001f, length), std::max(0.001f, body));
    state.manager->SetSpeed(handle, 0.72f + mids * 0.82f + highs * 0.38f);
}

void PlaceFlame(BridgeState& state, int index, float bass, float mids, float highs, float intensity)
{
    constexpr float pi = 3.14159265358979323846f;
    auto& handle = state.flameHandles[index];
    if (handle < 0 || !state.manager->Exists(handle))
    {
        const int startFrame = (index * 7) % 45;
        handle = state.manager->Play(state.fireEffect, Effekseer::Vector3D(0.0f, 0.0f, 0.0f), startFrame);
    }
    if (handle < 0) return;

    const float phase = index * 2.39996323f;
    const float angle = (static_cast<float>(index) / state.activeFlameCount) * pi * 2.0f;
    const float intensityDrive = std::clamp(0.55f + intensity * 0.65f, 0.55f, 2.45f);
    const float rawEnergy = std::clamp((bass * 0.58f + mids * 0.27f + highs * 0.15f) * intensityDrive, 0.0f, 1.0f);
    const float audioGate = std::clamp((rawEnergy - 0.012f) / 0.16f, 0.0f, 1.0f);
    const float bassLift = std::pow(std::clamp(bass * intensityDrive * 2.10f, 0.0f, 1.0f), 0.48f) * audioGate;
    const float breathing = std::sin(state.elapsed * (1.7f + mids * 2.8f) + phase) * (0.06f + mids * 0.12f);
    const float radius = (state.volumetricJet ? 3.55f : 4.42f) + bassLift * 0.10f;
    const float idleEmber = 0.018f;
    const float scaleX = state.volumetricJet
        ? idleEmber + audioGate * (0.42f + bassLift * 0.28f)
        : idleEmber + audioGate * (0.34f + bassLift * 0.27f);
    const float scaleY = state.volumetricJet
        ? scaleX * (1.0f + bassLift * 0.14f)
        : idleEmber + audioGate * (0.88f + bassLift * 1.52f + breathing * 0.38f);

    state.manager->SetLocation(handle, std::cos(angle) * radius, std::sin(angle) * radius, 0.0f);
    state.manager->SetRotation(handle, 0.0f, 0.0f, angle - pi * 0.5f);
    const float depthScale = state.volumetricJet ? scaleX : 0.22f + audioGate * 0.52f;
    state.manager->SetScale(handle, scaleX, std::max(idleEmber, scaleY), depthScale);
    state.manager->SetDynamicInput(handle, 0, bassLift);
    state.manager->SetDynamicInput(handle, 1, std::clamp(mids, 0.0f, 1.0f));
    state.manager->SetDynamicInput(handle, 2, std::clamp(highs, 0.0f, 1.0f));
    state.manager->SetDynamicInput(handle, 3, std::clamp(bassLift * 0.65f + highs * 0.35f, 0.0f, 1.0f));
    state.manager->SetSpeed(handle, 0.16f + audioGate * (0.72f + mids * intensityDrive * 1.35f + highs * 0.30f));
}

void PlaceSmoke(BridgeState& state, int index, float bass, float mids, float highs, float intensity)
{
    if (state.smokeEffect == nullptr) return;
    constexpr float pi = 3.14159265358979323846f;
    auto& handle = state.smokeHandles[index];
    if (handle < 0 || !state.manager->Exists(handle))
        handle = state.manager->Play(state.smokeEffect, Effekseer::Vector3D(0.0f, 0.0f, 0.0f), (index * 17) % 100);
    if (handle < 0) return;

    const float intensityDrive = std::clamp(0.60f + intensity * 0.48f, 0.60f, 1.90f);
    const float energy = std::clamp((bass * 0.48f + mids * 0.37f + highs * 0.15f) * intensityDrive, 0.0f, 1.0f);
    const float gate = std::clamp((energy - 0.025f) / 0.34f, 0.0f, 1.0f);
    const float angle = (static_cast<float>(index) / BridgeState::SmokeCount) * pi * 2.0f + 0.11f;
    const float radius = 4.34f;
    state.manager->SetLocation(handle, std::cos(angle) * radius, std::sin(angle) * radius, -0.08f);
    state.manager->SetRotation(handle, 0.0f, 0.0f, angle - pi * 0.5f);
    state.manager->SetScale(handle, 0.02f + gate * 0.15f, 0.03f + gate * 0.30f, 0.10f + gate * 0.16f);
    state.manager->SetSpeed(handle, 0.10f + gate * (0.24f + mids * 0.22f));
}

void SetupModules(BridgeState& state)
{
    state.manager->SetSpriteRenderer(state.renderer->CreateSpriteRenderer());
    state.manager->SetRibbonRenderer(state.renderer->CreateRibbonRenderer());
    state.manager->SetRingRenderer(state.renderer->CreateRingRenderer());
    state.manager->SetTrackRenderer(state.renderer->CreateTrackRenderer());
    state.manager->SetModelRenderer(state.renderer->CreateModelRenderer());
    state.manager->SetTextureLoader(state.renderer->CreateTextureLoader());
    state.manager->SetModelLoader(state.renderer->CreateModelLoader());
    state.manager->SetMaterialLoader(state.renderer->CreateMaterialLoader());
    state.manager->SetCurveLoader(Effekseer::MakeRefPtr<Effekseer::CurveLoader>());
}
}

HYPNIX_API void* __cdecl HypnixEfkCreate(ID3D11Device* device, ID3D11DeviceContext* context, int maxInstances)
{
    if (device == nullptr || context == nullptr) return nullptr;
    try
    {
        auto state = new BridgeState();
        state->manager = Effekseer::Manager::Create(std::max(1024, maxInstances));
        auto graphics = EffekseerRendererDX11::CreateGraphicsDevice(device, context);
        state->renderer = EffekseerRendererDX11::Renderer::Create(graphics, std::max(1024, maxInstances));
        if (state->manager == nullptr || state->renderer == nullptr)
        {
            delete state;
            return nullptr;
        }
        SetupModules(*state);
        state->manager->SetCoordinateSystem(Effekseer::CoordinateSystem::RH);
        return state;
    }
    catch (...)
    {
        return nullptr;
    }
}

HYPNIX_API int __cdecl HypnixEfkLoad(void* pointer, const wchar_t* effectPath)
{
    if (pointer == nullptr || effectPath == nullptr) return 0;
    auto state = static_cast<BridgeState*>(pointer);
    const std::wstring mainPath(effectPath);
    state->volumetricJet = mainPath.find(L"HypnixFlamethrower") != std::wstring::npos;
    state->activeFlameCount = state->volumetricJet ? 12 : BridgeState::FlameCount;
    state->fireEffect = Effekseer::Effect::Create(state->manager, reinterpret_cast<const char16_t*>(effectPath));
    if (state->fireEffect == nullptr) return 0;
    std::wstring smokePath(effectPath);
    const auto separator = smokePath.find_last_of(L"\\/");
    smokePath = (separator == std::wstring::npos ? std::wstring() : smokePath.substr(0, separator + 1)) +
                L"HypnixFireRingSmoke.efkefc";
    if (!state->volumetricJet)
        state->smokeEffect = Effekseer::Effect::Create(
            state->manager, reinterpret_cast<const char16_t*>(smokePath.c_str()));
    if (!state->volumetricJet)
        for (int i = 0; i < state->activeFlameCount; i++) PlaceFlame(*state, i, 0.0f, 0.0f, 0.0f, 1.0f);
    for (int i = 0; i < BridgeState::SmokeCount; i++) PlaceSmoke(*state, i, 0.0f, 0.0f, 0.0f, 1.0f);
    return state->fireEffect != nullptr ? 1 : 0;
}

HYPNIX_API void __cdecl HypnixEfkUpdate(void* pointer, float deltaSeconds, float bass, float mids, float highs, float intensity)
{
    if (pointer == nullptr) return;
    auto state = static_cast<BridgeState*>(pointer);
    const float delta = std::clamp(deltaSeconds, 0.0f, 0.05f);
    state->elapsed += delta;

    if (state->fireEffect != nullptr && state->volumetricJet)
    {
        const float drive = std::clamp(0.72f + intensity * 0.82f, 0.72f, 2.70f);
        const float jetEnergy = std::clamp((bass * 0.68f + mids * 0.23f + highs * 0.09f) * drive, 0.0f, 1.0f);
        const float onset = std::max(0.0f, jetEnergy - state->previousJetEnergy);
        state->jetEmissionCooldown = std::max(0.0f, state->jetEmissionCooldown - delta);

        const bool beat = jetEnergy > 0.11f && onset > 0.025f && state->jetEmissionCooldown <= 0.0f;
        const bool sustained = jetEnergy > 0.34f && state->jetEmissionCooldown <= 0.0f;
        if (beat || sustained)
        {
            StartVolumetricJet(*state, jetEnergy);
            if (jetEnergy > 0.72f) StartVolumetricJet(*state, jetEnergy * 0.92f);
            state->jetEmissionCooldown = std::clamp(0.24f - jetEnergy * 0.12f, 0.09f, 0.22f);
        }

        for (int i = 0; i < state->activeFlameCount; i++)
            UpdateVolumetricJet(*state, i, delta, bass, mids, highs, intensity);
        state->previousJetEnergy = state->previousJetEnergy * 0.68f + jetEnergy * 0.32f;
    }
    else if (state->fireEffect != nullptr)
    {
        for (int i = 0; i < state->activeFlameCount; i++)
            PlaceFlame(*state, i, bass, mids, highs, intensity);
    }
    if (state->smokeEffect != nullptr)
        for (int i = 0; i < BridgeState::SmokeCount; i++)
            PlaceSmoke(*state, i, bass, mids, highs, intensity);

    Effekseer::Manager::UpdateParameter update;
    update.DeltaFrame = std::clamp(delta * 60.0f, 0.0f, 3.0f);
    update.UpdateInterval = 1.0f;
    update.SyncUpdate = true;
    state->manager->Update(update);
}

HYPNIX_API void __cdecl HypnixEfkRender(void* pointer, float aspectRatio)
{
    if (pointer == nullptr) return;
    auto state = static_cast<BridgeState*>(pointer);

    Effekseer::Vector3D eye(0.0f, 0.0f, 18.0f);
    Effekseer::Matrix44 camera;
    camera.LookAtRH(eye, Effekseer::Vector3D(0.0f, 0.0f, 0.0f), Effekseer::Vector3D(0.0f, 1.0f, 0.0f));
    Effekseer::Matrix44 projection;
    projection.PerspectiveFovRH(52.0f / 180.0f * 3.14159265f, std::max(0.1f, aspectRatio), 1.0f, 500.0f);

    Effekseer::Manager::LayerParameter layer;
    layer.ViewerPosition = eye;
    state->manager->SetLayerParameter(0, layer);
    state->renderer->SetTime(state->elapsed);
    state->renderer->SetCameraMatrix(camera);
    state->renderer->SetProjectionMatrix(projection);
    state->renderer->BeginRendering();
    Effekseer::Manager::DrawParameter draw;
    draw.ZNear = 0.0f;
    draw.ZFar = 1.0f;
    draw.ViewProjectionMatrix = state->renderer->GetCameraProjectionMatrix();
    state->manager->Draw(draw);
    state->renderer->EndRendering();
}

HYPNIX_API void __cdecl HypnixEfkDestroy(void* pointer)
{
    if (pointer == nullptr) return;
    auto state = static_cast<BridgeState*>(pointer);
    for (auto handle : state->flameHandles)
        if (handle >= 0) state->manager->StopEffect(handle);
    for (auto handle : state->smokeHandles)
        if (handle >= 0) state->manager->StopEffect(handle);
    delete state;
}
