using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Egc;

/// <summary>
/// Camera orbită: privește mereu spre o țintă și se mișcă pe o sferă în jurul ei.
/// Poziția rezultă din distanță (rază), azimut (ca yaw) și elevație (ca pitch).
/// </summary>
public class OrbitCamera : Camera
{
    public Vector3 Target { get; set; }
    public float Distance { get; set; }
    public float Azimuth { get; set; }      // grade, în jurul axei Y
    public float Elevation { get; set; }    // grade, deasupra planului orizontal

    // --- Parametri ---
    public const float MaxElevation = 89.0f;     
    public const float MinDistance = 1.5f;
    public const float MaxDistance = 30.0f;
    public float MouseSensitivity = 0.3f;        // grade pe pixel
    public float KeyRotateSpeed = 90.0f;         // grade pe secundă (săgeți)
    public float ZoomStep = 0.5f;                // unități de lume pe un pas al rotiței

    private static readonly Vector3 StartTarget = new(0.0f, 1.0f, 0.0f);
    private const float StartDistance = 8.0f;
    private const float StartAzimuth = 90.0f;    // camera pe axa +Z, privind spre -Z
    private const float StartElevation = 20.0f;

    public OrbitCamera()
    {
        Reset();
    }

    public override string Name => "Orbită";

    public override Vector3 Position
    {
        get
        {
            float az = MathHelper.DegreesToRadians(Azimuth);
            float el = MathHelper.DegreesToRadians(Elevation);
            return Target + Distance * new Vector3(
                MathF.Cos(el) * MathF.Cos(az),
                MathF.Sin(el),
                MathF.Cos(el) * MathF.Sin(az));
        }
    }

    
    public override Vector3 Front => Vector3.Normalize(Target - Position);

    public override Matrix4 ViewMatrix => Matrix4.LookAt(Position, Target, Vector3.UnitY);

    public override void Update(KeyboardState keyboard, MouseState mouse, Vector2 mouseDelta, float dt)
    {
        // Mouse: clic stânga ținut + mișcare
        if (mouse.IsButtonDown(MouseButton.Left))
        {
            Azimuth += mouseDelta.X * MouseSensitivity;
            Elevation += mouseDelta.Y * MouseSensitivity;
        }

        // Săgeți
        float step = KeyRotateSpeed * dt;
        if (keyboard.IsKeyDown(Keys.Left)) Azimuth += step;
        if (keyboard.IsKeyDown(Keys.Right)) Azimuth -= step;
        if (keyboard.IsKeyDown(Keys.Up)) Elevation += step;
        if (keyboard.IsKeyDown(Keys.Down)) Elevation -= step;

        // Rotița: distanța (nu FOV-ul)
        if (mouse.ScrollDelta.Y != 0.0f)
        {
            Distance -= mouse.ScrollDelta.Y * ZoomStep;
        }

        Elevation = Math.Clamp(Elevation, -MaxElevation, MaxElevation);
        Distance = Math.Clamp(Distance, MinDistance, MaxDistance);
    }

    public override void Reset()
    {
        Target = StartTarget;
        Distance = StartDistance;
        Azimuth = StartAzimuth;
        Elevation = StartElevation;
        FieldOfView = 60.0f;
    }

    public override IEnumerable<string> Describe()
    {
        Vector3 p = Position;
        yield return $"Țintă          : ({Target.X,6:F2}, {Target.Y,6:F2}, {Target.Z,6:F2})";
        yield return $"Distanța       : {Distance,6:F2}";
        yield return $"Azimut / elev. : {Azimuth % 360.0f,7:F1}° / {Elevation,6:F1}°";
        yield return $"Poziție        : ({p.X,6:F2}, {p.Y,6:F2}, {p.Z,6:F2})";
    }
    public void LookFrom(Vector3 position, Vector3 front, float distance)
    {
        Distance = Math.Clamp(distance, MinDistance, MaxDistance);
        Target = position + front * Distance;

        Vector3 o = -front;    // vectorul unitar țintă -> cameră
        Elevation = MathHelper.RadiansToDegrees(MathF.Asin(o.Y));
        Azimuth = MathHelper.RadiansToDegrees(MathF.Atan2(o.Z, o.X));
        Elevation = Math.Clamp(Elevation, -MaxElevation, MaxElevation);
    }
}