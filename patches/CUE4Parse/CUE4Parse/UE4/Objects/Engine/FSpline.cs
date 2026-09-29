using System;
using System.Collections.Generic;
using CUE4Parse.UE4.Objects.Core.Math;
using CUE4Parse.UE4.Readers;

namespace CUE4Parse.UE4.Objects.Engine;

/// <summary>
/// A knot of a spline channel: its parameter and how many control values share it
/// (4 at a clamped end, 3 between the segments of a cubic poly-Bezier).
/// </summary>
public readonly struct FSplineKnot(float value, int multiplicity)
{
    public readonly float Value = value;
    public readonly int Multiplicity = multiplicity;
}

/// <summary>
/// One channel of the UE 5.6+ spline (the position curve, or a named attribute such as
/// Rotation or Scale): per point its arrive handle, value and leave handle, in that order,
/// then the knots and each point's interp mode.
/// </summary>
public class FSplineChannel
{
    public object[] Values = [];
    public FSplineKnot[] Knots = [];
    public byte[] InterpModes = [];

    /// <summary>A point's value (the middle of its three control values).</summary>
    public T Point<T>(int i) => (T) Values[i * 3 + 1];
    public int PointCount => Values.Length / 3;

    internal static void ReadHeader(FArchive Ar)
    {
        Ar.Read<int>();     // version
        Ar.Read<uint>();    // type id
        Ar.Read<ushort>();
    }

    public FSplineChannel(FArchive Ar, string? name)
    {
        ReadHeader(Ar);
        ReadHeader(Ar);
        var count = Ar.Read<int>();
        var size = name switch
        {
            null or "Position" or "Scale" => 24,
            "Rotation" => 32,
            _ => InferValueSize(Ar, count)
        };
        Values = new object[count];
        for (var i = 0; i < count; i++)
            Values[i] = size switch
            {
                24 => new FVector(Ar),
                32 => new FQuat(Ar),
                8 => Ar.Read<double>(),
                16 => new FVector2D(Ar),
                _ => Ar.ReadBytes(size)
            };
        Knots = Ar.ReadArray(() => new FSplineKnot(Ar.Read<float>(), Ar.Read<int>()));
        Ar.Read<int>();
        Ar.ReadArray<float>();
        Ar.Read<int>();
        InterpModes = Ar.ReadArray<byte>();
        Ar.Read<int>();
    }

    /// <summary>An attribute channel of an unknown type: the value size its knots follow.</summary>
    private static int InferValueSize(FArchive Ar, int count)
    {
        var start = Ar.Position;
        try
        {
            foreach (var size in (int[]) [24, 32, 8, 16, 4, 12])
            {
                if (start + (long) count * size + 4 > Ar.Length) continue;
                Ar.Position = start + (long) count * size;
                var knots = Ar.Read<int>();
                if (knots < 1 || knots > count + 1 || Ar.Position + knots * 8L > Ar.Length) continue;
                var ok = true;
                var last = float.NegativeInfinity;
                for (var k = 0; k < knots && ok; k++)
                {
                    var value = Ar.Read<float>();
                    var multiplicity = Ar.Read<int>();
                    ok = multiplicity is >= 1 and <= 4 && value > last && float.IsFinite(value);
                    last = value;
                }
                if (ok) return size;
            }
        }
        finally
        {
            Ar.Position = start;
        }
        throw new NotSupportedException("FSpline attribute channel of an unknown value type");
    }
}

public struct FSpline : IUStruct
{
    /// <summary>The spline's positions (UE 5.6+ "new" spline, else null).</summary>
    public FSplineChannel? Position;
    /// <summary>Its attribute channels by name (Rotation, Scale...).</summary>
    public Dictionary<string, FSplineChannel>? Attributes;

    public FSpline(FArchive Ar)
    {
        var previousImpl  = Ar.Read<sbyte>();

        var wasEnabled = previousImpl  != 0;
        var wasLegacy = previousImpl == 1;

        if (!wasEnabled) return;
        if (wasLegacy)
        {
            // TODO:
            throw new NotSupportedException("Further serialization of FSpline Struct type is currently not supported");
        }

        FSplineChannel.ReadHeader(Ar);
        Position = new FSplineChannel(Ar, null);
        var attributes = Ar.Read<int>();
        Attributes = new Dictionary<string, FSplineChannel>(attributes);
        for (var i = 0; i < attributes; i++)
        {
            var name = Ar.ReadFName().Text;
            Ar.Read<int>();
            Ar.Read<byte>();
            Ar.Read<float>();
            Attributes[name] = new FSplineChannel(Ar, name);
        }
        Ar.Read<int>();     // 10 (the reparameterization's steps per segment?)
    }
}
