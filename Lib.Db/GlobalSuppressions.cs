using System.Runtime.CompilerServices;

// ============================================================================
// [0] Micro-Optimization (Global)
// ----------------------------------------------------------------------------
// 모듈 내 모든 메서드의 로컬 변수(stackalloc 포함) 0 초기화 생략.
// 고성능 I/O 버퍼링 시 CPU 사이클 절약. (안전성 검증 완료: SharedMemoryCache 등)
// ============================================================================
[module: SkipLocalsInit]

// Broad ILLink and NativeAOT suppressions are intentionally not applied at
// assembly scope. AOT-incompatible JIT convenience APIs must carry local
// annotations or targeted suppressions with a smoke-test-backed invariant.
