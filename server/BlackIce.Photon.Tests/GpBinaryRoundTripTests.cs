using System.Collections;
using BlackIce.Photon;
using Xunit;

namespace BlackIce.Photon.Tests;

/// <summary>
/// Property-style round-trip tests for the GpBinary codec and message envelopes: for a large,
/// randomly-generated population of values, <c>decode(encode(x))</c> must reconstruct <c>x</c>.
///
/// This is the dependency-free equivalent of property-based testing (the kind a library such as
/// CsCheck would drive): a deterministic seeded generator produces thousands of values — scalars at
/// their encoding boundaries (zero/±1/min/max, NaN/±Inf), strings, byte/int arrays, custom types, and
/// nested object-arrays/hashtables — and a structural comparer verifies the round trip. A failure
/// reports the seed, iteration, value, and wire hex so it reproduces deterministically.
///
/// Self-contained: it exercises only our clean-room codec, so it runs without the Photon oracle DLL
/// and complements both the oracle cross-checks (ground-truth byte layout) and the hostile-input
/// hardening tests (malformed input is rejected, not crashed).
/// </summary>
public class GpBinaryRoundTripTests
{
    // A handful of fixed seeds keeps the run deterministic (reproducible failures) while still
    // covering a wide population — ~6k typed values and ~2.4k request envelopes per CI run.
    public static IEnumerable<object[]> Seeds =>
        new[] { 1, 7, 42, 1337, 90210, 2_147_483 }.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Typed_values_round_trip(int seed)
    {
        var rng = new Random(seed);
        for (int i = 0; i < 1000; i++)
        {
            object? value = GenValue(rng, depth: 4);

            byte[] bytes;
            object? decoded;
            try
            {
                bytes = new GpBinaryWriter().WriteTyped(value).ToArray();
                decoded = new GpBinaryReader(bytes).ReadTyped();
            }
            catch (Exception ex)
            {
                Assert.Fail($"seed {seed}, iter {i}: codec threw on {Describe(value)} — " +
                            $"{ex.GetType().Name}: {ex.Message}");
                return; // unreachable; satisfies definite-assignment
            }

            if (!GpEqual(value, decoded))
                Assert.Fail($"seed {seed}, iter {i}: round-trip mismatch\n" +
                            $"  in : {Describe(value)}\n  out: {Describe(decoded)}\n" +
                            $"  hex: {Convert.ToHexString(bytes)}");
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Operation_requests_round_trip(int seed)
    {
        var rng = new Random(seed);
        for (int i = 0; i < 400; i++)
        {
            byte op = (byte)rng.Next(256);
            int count = rng.Next(0, 16);              // parameter-table count is a single byte
            var parameters = new Dictionary<byte, object>();
            for (int k = 0; k < count; k++)
                parameters[(byte)rng.Next(256)] = GenValue(rng, depth: 3)!;

            var bytes = MessageSerializer.SerializeRequest(new OperationRequest(op, parameters));
            var back = MessageSerializer.DeserializeRequest(bytes);

            Assert.Equal(op, back.OperationCode);
            Assert.Equal(parameters.Count, back.Parameters.Count);
            foreach (var kv in parameters)
            {
                Assert.True(back.Parameters.ContainsKey(kv.Key),
                    $"seed {seed}, iter {i}: missing parameter key {kv.Key}");
                Assert.True(GpEqual(kv.Value, back.Parameters[kv.Key]),
                    $"seed {seed}, iter {i}: parameter {kv.Key} mismatch: " +
                    $"{Describe(kv.Value)} != {Describe(back.Parameters[kv.Key])}");
            }
        }
    }

    // --- value generator -------------------------------------------------------------------------
    // Produces only the runtime types GpBinaryWriter.WriteTyped supports. Containers recurse with a
    // decreasing depth budget so generation always terminates.

    private static object? GenValue(Random rng, int depth)
    {
        int categories = depth > 0 ? 14 : 12;   // 12 = object-array, 13 = hashtable (need depth)
        return rng.Next(categories) switch
        {
            0 => null,
            1 => rng.Next(2) == 0,
            2 => (byte)rng.Next(256),
            3 => (short)GenInt(rng),
            4 => GenInt(rng),
            5 => (long)GenInt(rng) * GenInt(rng),
            6 => GenFloat(rng),
            7 => GenDouble(rng),
            8 => GenString(rng),
            9 => GenBytes(rng),
            10 => GenIntArray(rng),
            11 => GenCustom(rng),
            12 => GenObjectArray(rng, depth),
            _ => GenHashtable(rng, depth),
        };
    }

    // Bias toward the values that exercise zig-zag/varint boundaries, plus a full-range draw.
    private static int GenInt(Random rng) => rng.Next(7) switch
    {
        0 => 0,
        1 => 1,
        2 => -1,
        3 => int.MaxValue,
        4 => int.MinValue,
        5 => rng.Next(-1000, 1000),
        _ => unchecked((rng.Next() << 1) ^ rng.Next()),
    };

    private static float GenFloat(Random rng) => rng.Next(6) switch
    {
        0 => 0f,
        1 => -0f,
        2 => float.NaN,
        3 => float.PositiveInfinity,
        4 => float.NegativeInfinity,
        _ => (float)((rng.NextDouble() - 0.5) * float.MaxValue),
    };

    private static double GenDouble(Random rng) => rng.Next(6) switch
    {
        0 => 0d,
        1 => -0d,
        2 => double.NaN,
        3 => double.PositiveInfinity,
        4 => double.NegativeInfinity,
        _ => (rng.NextDouble() - 0.5) * double.MaxValue,
    };

    // BMP, surrogate-free chars so every string survives UTF-8 encode/decode unchanged.
    private static string GenString(Random rng)
    {
        int len = rng.Next(0, 9);
        var chars = new char[len];
        for (int i = 0; i < len; i++) chars[i] = (char)rng.Next(0x20, 0x250);
        return new string(chars);
    }

    private static byte[] GenBytes(Random rng)
    {
        var b = new byte[rng.Next(0, 9)];
        rng.NextBytes(b);
        return b;
    }

    private static int[] GenIntArray(Random rng)
    {
        var a = new int[rng.Next(0, 7)];
        for (int i = 0; i < a.Length; i++) a[i] = GenInt(rng);
        return a;
    }

    // Any code 0..255 round-trips: writer emits the slim form (128+code) for codes <= 100 and the
    // explicit Custom(19)+code form above that; the reader accepts both.
    private static PhotonCustomData GenCustom(Random rng)
    {
        var data = new byte[rng.Next(0, 7)];
        rng.NextBytes(data);
        return new PhotonCustomData((byte)rng.Next(256), data);
    }

    private static object?[] GenObjectArray(Random rng, int depth)
    {
        var a = new object?[rng.Next(0, 5)];
        for (int i = 0; i < a.Length; i++) a[i] = GenValue(rng, depth - 1);
        return a;
    }

    private static Dictionary<object, object?> GenHashtable(Random rng, int depth)
    {
        var d = new Dictionary<object, object?>();
        int count = rng.Next(0, 5);
        for (int i = 0; i < count; i++)
        {
            // Keys are non-null leaves (string or byte); values may be any generated value (incl. null).
            object key = rng.Next(2) == 0 ? GenString(rng) : (byte)rng.Next(256);
            d[key] = GenValue(rng, depth - 1);
        }
        return d;
    }

    // --- structural comparison -------------------------------------------------------------------
    // Compares a written value against its decoded form, accounting for the reader's read-back types
    // (object[] -> object?[], any IDictionary -> Dictionary<object,object>) and float/double bit
    // identity (so NaN and -0.0 compare equal to themselves).

    private static bool GpEqual(object? a, object? b)
    {
        if (a is null) return b is null;
        if (b is null) return false;

        switch (a)
        {
            case bool:
            case byte:
            case short:
            case int:
            case long:
            case string:
                return a.Equals(b);

            case float f:
                return b is float bf && BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits(bf);
            case double d:
                return b is double bd && BitConverter.DoubleToInt64Bits(d) == BitConverter.DoubleToInt64Bits(bd);

            case byte[] ba:
                return b is byte[] bb && ba.SequenceEqual(bb);
            case int[] ia:
                return b is int[] ib && ia.SequenceEqual(ib);
            case PhotonCustomData pc:
                return b is PhotonCustomData qc && pc.Code == qc.Code && pc.Data.SequenceEqual(qc.Data);

            case object?[] arr:
                if (b is not object?[] barr || arr.Length != barr.Length) return false;
                for (int i = 0; i < arr.Length; i++)
                    if (!GpEqual(arr[i], barr[i])) return false;
                return true;

            case IDictionary dict:
                if (b is not IDictionary bdict || dict.Count != bdict.Count) return false;
                foreach (DictionaryEntry e in dict)
                {
                    if (!bdict.Contains(e.Key)) return false;
                    if (!GpEqual(e.Value, bdict[e.Key])) return false;
                }
                return true;

            default:
                return a.Equals(b);
        }
    }

    // --- diagnostics -----------------------------------------------------------------------------

    private static string Describe(object? v) => v switch
    {
        null => "null",
        string s => $"\"{s}\"",
        byte[] ba => $"byte[{ba.Length}]",
        int[] ia => $"int[{string.Join(",", ia)}]",
        PhotonCustomData c => $"custom(code={c.Code},len={c.Data.Length})",
        object?[] arr => "[" + string.Join(", ", arr.Select(Describe)) + "]",
        IDictionary d => "{" + string.Join(", ", d.Cast<DictionaryEntry>()
                                                   .Select(e => $"{Describe(e.Key)}={Describe(e.Value)}")) + "}",
        _ => $"{v} ({v.GetType().Name})",
    };
}
