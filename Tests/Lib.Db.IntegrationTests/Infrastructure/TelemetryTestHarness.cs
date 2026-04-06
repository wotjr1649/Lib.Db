// ============================================================================
// 파일: Infrastructure/TelemetryTestHarness.cs
// 설명: 텔레메트리 측정값 캡처용 테스트 하네스
// 대상: .NET 10 / C# 14
// ============================================================================

using System.Diagnostics.Metrics;

namespace Lib.Db.IntegrationTests.Infrastructure;

/// <summary>
/// 캡처된 측정값을 나타내는 레코드.
/// </summary>
public record CapturedMeasurement<T>(string InstrumentName, T Value, KeyValuePair<string, object?>[] Tags) where T : struct;

/// <summary>
/// MeterListener를 사용하여 테스트 중 메트릭을 캡처하는 하네스.
/// </summary>
public sealed class TelemetryTestHarness : IDisposable
{
    private readonly MeterListener _listener;
    private readonly List<CapturedMeasurement<double>> _doubles = new();
    private readonly List<CapturedMeasurement<long>> _longs = new();
    private readonly List<CapturedMeasurement<int>> _ints = new();

    public TelemetryTestHarness(string meterName)
    {
        _listener = new MeterListener();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            lock (_doubles)
                _doubles.Add(new CapturedMeasurement<double>(instrument.Name, measurement, tags.ToArray()));
        });

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            lock (_longs)
                _longs.Add(new CapturedMeasurement<long>(instrument.Name, measurement, tags.ToArray()));
        });

        _listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            lock (_ints)
                _ints.Add(new CapturedMeasurement<int>(instrument.Name, measurement, tags.ToArray()));
        });

        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    public IReadOnlyList<CapturedMeasurement<double>> GetDoubles(string name)
    {
        lock (_doubles) return _doubles.Where(x => x.InstrumentName == name).ToList();
    }

    public IReadOnlyList<CapturedMeasurement<long>> GetLongs(string name)
    {
        lock (_longs) return _longs.Where(x => x.InstrumentName == name).ToList();
    }

    public IReadOnlyList<CapturedMeasurement<int>> GetInts(string name)
    {
        lock (_ints) return _ints.Where(x => x.InstrumentName == name).ToList();
    }
}
