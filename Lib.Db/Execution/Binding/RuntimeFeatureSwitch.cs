// ============================================================================
// File: Lib.Db/Execution/Binding/RuntimeFeatureSwitch.cs
// Role: Narrow internal seam for runtime capability decisions in tests
// ============================================================================

#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Lib.Db.Execution.Binding;

internal static class RuntimeFeatureSwitch
{
    private static readonly AsyncLocal<bool?> s_dynamicCodeSupportedOverride = new();

    internal static bool? DynamicCodeSupportedOverride
        => s_dynamicCodeSupportedOverride.Value;

    [FeatureGuard(typeof(RequiresDynamicCodeAttribute))]
    internal static bool IsRuntimeDynamicCodeSupported
        => RuntimeFeature.IsDynamicCodeSupported;

    internal static bool IsDynamicCodeSupported
        => s_dynamicCodeSupportedOverride.Value is false
            ? false
            : IsRuntimeDynamicCodeSupported;

    internal static IDisposable OverrideDynamicCodeSupportedForTests(bool value)
    {
        bool? previous = s_dynamicCodeSupportedOverride.Value;
        s_dynamicCodeSupportedOverride.Value = value;
        return new ResetScope(previous);
    }

    private sealed class ResetScope(bool? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            s_dynamicCodeSupportedOverride.Value = previous;
            _disposed = true;
        }
    }
}
