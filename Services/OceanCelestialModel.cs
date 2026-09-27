using System.Numerics;
using CosineKitty;

namespace AnimatedWallPaper.Services;

internal enum OceanSkyComposition { Geographic, Cinematic }
internal enum OceanAir { Clear, Maritime, Hazy }

internal sealed record OceanCelestialSettings(
    DateTimeOffset EpochUtc,
    double TimeScale = 24,
    double Latitude = 38.72,
    double Longitude = -9.14,
    double CameraAzimuth = 90,
    OceanSkyComposition Composition = OceanSkyComposition.Cinematic,
    OceanAir Air = OceanAir.Maritime)
{
    public static OceanCelestialSettings Default => new(new DateTimeOffset(2026, 9, 27, 6, 35, 0, TimeSpan.Zero));
    public OceanCelestialSettings Normalize() => this with
    {
        EpochUtc = EpochUtc.Year is >= 1900 and <= 2100 ? EpochUtc.ToUniversalTime() : Default.EpochUtc,
        TimeScale = double.IsFinite(TimeScale) ? Math.Clamp(TimeScale, 1, 144) : 24,
        Latitude = double.IsFinite(Latitude) ? Math.Clamp(Latitude, -89.9, 89.9) : 38.72,
        Longitude = double.IsFinite(Longitude) ? Math.Clamp(Longitude, -180, 180) : -9.14,
        CameraAzimuth = double.IsFinite(CameraAzimuth) ? (CameraAzimuth % 360 + 360) % 360 : 90,
        Composition = Enum.IsDefined(Composition) ? Composition : OceanSkyComposition.Cinematic,
        Air = Enum.IsDefined(Air) ? Air : OceanAir.Maritime
    };
}

// Owned by the output, never by a GPU resource. Speed changes integrate the old rate first.
internal sealed class OceanCelestialClock(DateTimeOffset epoch)
{
    private double _last;
    private bool _initialized, _paused;
    public DateTimeOffset Utc { get; private set; } = epoch;
    public double Rate { get; private set; } = 24;
    public DateTimeOffset Advance(double wallSeconds)
    {
        if (!double.IsFinite(wallSeconds) || wallSeconds < 0 || (_initialized && wallSeconds < _last)) return Utc;
        if (_initialized && !_paused) Utc = Utc.AddSeconds((wallSeconds - _last) * Rate);
        _last = wallSeconds; _initialized = true; return Utc;
    }
    public void SetRate(double rate, double wallSeconds) { Advance(wallSeconds); Rate = double.IsFinite(rate) ? Math.Clamp(rate, 1, 144) : 24; }
    public void SetPaused(bool paused, double wallSeconds) { Advance(wallSeconds); _paused = paused; }
    public void Seek(DateTimeOffset utc, double wallSeconds) { Advance(wallSeconds); Utc = utc.ToUniversalTime(); }
}

internal sealed record OceanCelestialBody(Vector3 Direction, double AirlessElevation, double Azimuth,
    double DistanceKm, float Radius, float ApparentElevation, float VerticalScale, Vector3 Irradiance);

internal sealed record OceanCelestialFrame(DateTimeOffset Utc, OceanCelestialBody Sun, OceanCelestialBody Moon,
    Vector3 MoonLight, Vector3 MoonPrime, Vector3 MoonEast, Vector3 MoonNorth,
    float MoonMaterialIntegral, float PhaseAngle, float Exposure, float Daylight, float CloudCover,
    OceanCelestialSettings Settings)
{
    public Vector3 MoonRadiance => Moon.Irradiance / (MathF.PI * Moon.Radius * Moon.Radius * Math.Max(.00001f, MoonMaterialIntegral));
    public Vector3 SunRadiance => Sun.Irradiance / (MathF.PI * Sun.Radius * Sun.Radius);
}

internal static class OceanCelestialModel
{
    public const double Deg = Math.PI / 180;
    public const double AuKm = 149597870.7;
    private static Vector3 V(AstroVector v) => new((float)v.x, (float)v.y, (float)v.z);
    private static Vector3 Local(AstroVector v) => new((float)-v.y, (float)v.z, (float)v.x); // East, Up, North
    public static double Refraction(double geometricDegrees) => Astronomy.RefractionAngle(CosineKitty.Refraction.Normal, Math.Clamp(geometricDegrees, -90, 90));
    public static float Smooth(float a, float b, float x) { var t = Math.Clamp((x-a)/(b-a), 0, 1); return t*t*(3-2*t); }

    public static double PhaseFlux(double radians)
    {
        var degrees = Math.Clamp(radians / Deg, 0, 180);
        return Math.Pow(10, -.4 * (.026 * degrees + 4e-9 * Math.Pow(degrees, 4))) *
            (1 + .35 * Math.Exp(-Math.Pow(degrees / 3, 2))) / 1.35;
    }
    // Analytic integral of the existing LS/Lambert mixture, divided by its full-phase value.
    public static double MaterialIntegral(double a)
    {
        if (a < 1e-6) return 1;
        if (a > Math.PI - 1e-5) return 0;
        var ls = 1 - Math.Sin(a/2) * Math.Tan(a/2) * Math.Log(1/Math.Tan(a/4));
        var lambert = (Math.Sin(a) + (Math.PI-a)*Math.Cos(a))/Math.PI;
        return Math.Max(0, (.8*ls + .2*(2d/3)*lambert)/(.8+.2*2/3));
    }

    public static OceanCelestialFrame Evaluate(DateTimeOffset utc, OceanCelestialSettings options)
    {
        options = options.Normalize();
        var time = new AstroTime(utc.UtcDateTime);
        var observer = new Observer(options.Latitude, options.Longitude, 2.8);
        var rotation = Astronomy.Rotation_EQJ_HOR(time, observer);
        var sunEq = Astronomy.Equator(Body.Sun, time, observer, EquatorEpoch.J2000, Aberration.Corrected);
        var moonEq = Astronomy.Equator(Body.Moon, time, observer, EquatorEpoch.J2000, Aberration.Corrected);
        var sunLocal = Local(Astronomy.RotateVector(rotation, sunEq.vec));
        var moonLocal = Local(Astronomy.RotateVector(rotation, moonEq.vec));
        var sunUnit = Vector3.Normalize(sunLocal); var moonUnit = Vector3.Normalize(moonLocal);
        var solarAtMoon = Vector3.Normalize(sunLocal - moonLocal);
        var phase = Math.Acos(Math.Clamp(Vector3.Dot(-moonUnit, solarAtMoon), -1, 1));
        var sunEnergy = new Vector3(6.2f, 6.0f, 5.8f) * (float)(1/(sunEq.dist*sunEq.dist));
        var moonDistance = moonEq.dist*AuKm;
        var moonEnergy = new Vector3(6.2f, 6.0f, 5.8f) / 398107.17f *
            (float)(PhaseFlux(phase)*Math.Pow(384400/moonDistance, 2));
        var sun = MakeBody(sunUnit, sunEq.dist*AuKm, 695700, sunEnergy, options);
        var moon = MakeBody(moonUnit, moonDistance, 1737.4, moonEnergy, options);
        var axisX = SafeRight(moonUnit); var axisY = Vector3.Cross(moonUnit, axisX);
        Vector3 Project(Vector3 v) => new(Vector3.Dot(v,axisX), Vector3.Dot(v,axisY), Vector3.Dot(v,-moonUnit));
        var axis = Astronomy.RotationAxis(Body.Moon, time);
        var pole = Vector3.Normalize(V(axis.north));
        var node = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, pole));
        var equator90 = Vector3.Cross(pole, node);
        var spin = (float)((axis.spin % 360)*Deg);
        Vector3 ToLocal(Vector3 v) => Local(Astronomy.RotateVector(rotation, new AstroVector(v.X,v.Y,v.Z,time)));
        var prime = ToLocal(node*MathF.Cos(spin) + equator90*MathF.Sin(spin));
        var east = ToLocal(-node*MathF.Sin(spin) + equator90*MathF.Cos(spin));
        var north = ToLocal(pole);
        var daylight = Smooth(-12, 1, (float)sun.AirlessElevation);
        // Deliberate photographic adaptation, in scene-linear units. It does not recolor the Moon.
        var depression=Math.Max(0,-(float)sun.AirlessElevation);
        var exposure=Math.Min(16000,1.1f*MathF.Exp(Math.Min(10,.5f*depression*depression/(depression+1))));
        return new(utc, sun, moon, Project(solarAtMoon), Project(prime), Project(east), Project(north),
            (float)MaterialIntegral(phase), (float)phase, exposure, daylight,
            options.Air switch { OceanAir.Clear => .15f, OceanAir.Hazy => .38f, _ => .29f }, options);
    }

    public static Vector3 SafeRight(Vector3 direction) => Vector3.Normalize(Vector3.Cross(Math.Abs(direction.Y) > .9999f ? Vector3.UnitZ : Vector3.UnitY, direction));

    private static OceanCelestialBody MakeBody(Vector3 local, double distance, double radiusKm, Vector3 energy, OceanCelestialSettings options)
    {
        var altitude = Math.Asin(Math.Clamp(local.Y,-1,1))/Deg;
        var azimuth = (Math.Atan2(local.X, local.Z)/Deg + 360)%360;
        var r = Math.Asin(radiusKm/distance);
        var hApp = altitude + Refraction(altitude);
        var lower = altitude-r/Deg; var upper = altitude+r/Deg;
        var vertical = (float)(1+(Refraction(upper)-Refraction(lower))/(2*r/Deg));
        // Cinematic projection preserves altitude while bringing east/west arcs into the frame.
        var az = options.Composition == OceanSkyComposition.Cinematic ?
            -.46*Math.Sin(azimuth*Deg) : (azimuth-options.CameraAzimuth)*Deg;
        var elevation = altitude*Deg;
        var direction = new Vector3((float)(Math.Sin(az)*Math.Cos(elevation)),(float)Math.Sin(elevation),(float)(Math.Cos(az)*Math.Cos(elevation)));
        return new(direction, altitude, azimuth, distance, (float)r, (float)(hApp*Deg), Math.Clamp(vertical,.6f,1.02f), energy);
    }

    public static DateTimeOffset? FindEvent(OceanCelestialSettings settings, DateTimeOffset date, bool moon, bool rising)
    {
        settings = settings.Normalize();
        var result = Astronomy.SearchRiseSet(moon ? Body.Moon : Body.Sun,
            new Observer(settings.Latitude, settings.Longitude, 2.8), rising ? Direction.Rise : Direction.Set,
            new AstroTime(date.UtcDateTime.Date), 2, 0);
        return result is null ? null : new DateTimeOffset(result.ToUtcDateTime(), TimeSpan.Zero);
    }
}
