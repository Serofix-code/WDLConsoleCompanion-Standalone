using System;

namespace WDLConsoleCompanion.Services;

internal static class TeleportFeatureBridge
{
    internal static string MoveForward(TrainerSession session, float distance)
    {
        Validate(distance);
        FacingQuery facing = session.QueryFacingForFly();
        GamePosition current = session.ReadCurrentTeleportPosition();
        float dx, dz;
        if (facing.Reticle is GamePosition target)
        {
            dx = target.X - current.X;
            dz = target.Z - current.Z;
        }
        else
        {
            float angle = facing.AngleDegrees ?? throw new InvalidOperationException("The game did not publish a facing direction.");
            float radians = angle * MathF.PI / 180f;
            dx = MathF.Cos(radians);
            dz = MathF.Sin(radians);
        }
        float length = MathF.Sqrt(dx * dx + dz * dz);
        if (!float.IsFinite(length) || length < 0.01f)
            throw new InvalidOperationException("Aim into open space so forward direction can be resolved.");
        return session.TeleportTo(new GamePosition(current.X + dx / length * distance, current.Y, current.Z + dz / length * distance));
    }

    internal static string MoveSideways(TrainerSession session, float distance)
    {
        Validate(distance);
        FacingQuery facing = session.QueryFacingForFly();
        GamePosition current = session.ReadCurrentTeleportPosition();
        float dx, dz;
        if (facing.Reticle is GamePosition target) { dx = target.X - current.X; dz = target.Z - current.Z; }
        else
        {
            float angle = facing.AngleDegrees ?? throw new InvalidOperationException("The game did not publish a facing direction.");
            float radians = angle * MathF.PI / 180f;
            dx = MathF.Cos(radians); dz = MathF.Sin(radians);
        }
        float length = MathF.Sqrt(dx * dx + dz * dz);
        if (!float.IsFinite(length) || length < 0.01f)
            throw new InvalidOperationException("Aim into open space so sideways direction can be resolved.");
        float rightX = dz / length;
        float rightZ = -dx / length;
        return session.TeleportTo(new GamePosition(current.X + rightX * distance, current.Y, current.Z + rightZ * distance));
    }

    internal static string MoveVertical(TrainerSession session, float distance)
    {
        Validate(distance);
        GamePosition current = session.ReadCurrentTeleportPosition();
        return session.TeleportTo(new GamePosition(current.X, current.Y + distance, current.Z));
    }

    private static void Validate(float distance)
    {
        if (!float.IsFinite(distance) || MathF.Abs(distance) <= 0.0001f || MathF.Abs(distance) > 50f)
            throw new ArgumentOutOfRangeException(nameof(distance), "Fly step must be finite and between 0 and 50 metres.");
    }
}
