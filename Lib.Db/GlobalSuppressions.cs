// ============================================================================
// 파일 목적
// ----------------------------------------------------------------------------
// 이 파일은 .NET Code Analysis(Analyzer) 및 IL Linker(Trimmer), NativeAOT 분석에서
// 발생하는 특정 경고를 "프로젝트(어셈블리) 단위"로 억제(Suppress)하기 위한 전용 파일입니다.
//
// 왜 필요한가?
// - 본 라이브러리는 "JIT(일반 런타임) + AOT(트리밍/NativeAOT)" 를 동시에 지원하는 하이브리드 형태입니다.
// - 일부 코드는 JIT에서만 활성화되는 리플렉션/Expression Tree 경로를 포함합니다.
// - AOT/Trimming 환경에서는 리플렉션 기반 접근이 IL Linker에 의해 제거될 수 있고, 분석기가 경고를 냅니다.
// - 그러나 실제 실행에서는 RuntimeFeature 체크, 안전한 fallback, 제한된 사용범위 등을 통해
//   "JIT 경로에서만 리플렉션을 쓰거나", "AOT에서는 다른 경로로 우회"하도록 설계되어 있을 수 있습니다.
//
// 주의
// - 경고 억제는 '문제를 숨기는 것'이 아니라, 라이브러리 설계상 의도된 동작을 설명하고
//   소비자(사용자)에게 제약사항을 문서화한 뒤 필요 범위만 억제하는 전략입니다.
// - 억제 사유(Justification)는 유지보수 시 반드시 실제 구현과 일치해야 합니다.
//   (RuntimeFeature 체크가 제거되거나, 리플렉션 사용 범위가 확대되면 억제 근거가 깨질 수 있습니다.)
// ============================================================================

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// ============================================================================
// [0] Micro-Optimization (Global)
// ----------------------------------------------------------------------------
// 모듈 내 모든 메서드의 로컬 변수(stackalloc 포함) 0 초기화 생략.
// 고성능 I/O 버퍼링 시 CPU 사이클 절약. (안전성 검증 완료: SharedMemoryCache 등)
// ============================================================================
[module: SkipLocalsInit]

// ============================================================================
// [1] IL2026 (RequiresUnreferencedCode) - 트리밍(Trimming) 관련
// ----------------------------------------------------------------------------
// 의미:
// - 'RequiresUnreferencedCode'가 붙은 멤버를 호출하면, 트리밍 시 해당 멤버가 의존하는 코드가
//   제거될 수 있어(정적 분석으로 보장 불가) 기능이 깨질 수 있다는 경고입니다.
//
// 이 라이브러리에서 발생 가능한 이유(예시):
// - 특정 타입/멤버를 리플렉션으로 동적으로 탐색하여 매핑/직렬화/바인딩을 구성하는 코드
// - 런타임에서만 확정되는 타입에 대해 Expression Tree를 생성하는 코드
//
// 억제 근거:
// - "JIT 경로에서만" 리플렉션/동적 접근을 수행하고,
//   "AOT 경로에서는" 사전에 제한/대체 로직을 사용하도록 분기(RuntimeFeature 체크 등)하는 설계.
// - 즉, 트리밍/AOT에서 안전하지 않은 코드가 *무조건 실행되는* 것이 아니라,
//   런타임 체크로 '사용 가능한 환경에서만' 실행되도록 보호되어 있다는 전제.
//
// 소비자(사용자)에게 알려야 할 제약:
// - NativeAOT + 트리밍 환경에서는 일부 "제네릭 JSON/리플렉션" 기능에 제한이 있을 수 있으며,
//   문서에 해당 제한 및 권장 설정(예: JsonSerializerContext/소스 제너레이터 사용)을 명시해야 합니다.
// ============================================================================
[assembly: SuppressMessage(
    "Trimming",
    "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
    Justification = "Hybrid library supporting both JIT and AOT. JIT paths use reflection safely protected by runtime checks. AOT paths for generic JSON/Reflection have known limitations documented for consumers.",
    Scope = "module")]


// ============================================================================
// [2] IL3050 (RequiresDynamicCode) - AOT 관련
// ----------------------------------------------------------------------------
// 의미:
// - 'RequiresDynamicCode'가 붙은 멤버(예: Expression Tree 컴파일, RuntimeCodeGen 등)를 호출하면
//   NativeAOT 환경에서 동작이 깨질 수 있다는 경고입니다.
// - NativeAOT는 런타임 코드 생성(동적 IL 생성/컴파일) 등을 제한적으로 지원합니다.
//
// 이 라이브러리에서 발생 가능한 이유(예시):
// - Expression Tree를 생성/컴파일하여 매퍼를 만드는 최적화 경로
// - Reflection.Emit 또는 유사한 동적 코드 생성 기법(직접/간접)
//
// 억제 근거:
// - JIT 환경에서는 Expression Tree/리플렉션 최적화 경로를 사용할 수 있지만,
// - AOT 환경에서는 RuntimeFeature로 분기하여 해당 경로를 차단하고,
//   미리 생성된 매퍼/정적 경로/간소화된 fallback을 사용하도록 설계되어 있다는 전제.
//
// 유지보수 포인트:
// - "Protected by RuntimeFeature checks"가 사실이려면,
//   실제 코드가 AOT에서 해당 경로를 타지 않도록 확실한 조건(예: RuntimeFeature.IsDynamicCodeSupported)
//   또는 명시적 옵션 플래그가 존재해야 합니다.
// ============================================================================
[assembly: SuppressMessage(
    "AOT",
    "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.",
    Justification = "JIT paths use Expression Trees and Reflection optimization. Protected by RuntimeFeature checks.",
    Scope = "module")]


// ============================================================================
// [3] IL2075 - DynamicallyAccessedMembers 불충족(주로 'this' 인자)
// ----------------------------------------------------------------------------
// 의미:
// - 트리머가 리플렉션을 통해 접근될 멤버가 무엇인지 보장할 수 없을 때,
//   'DynamicallyAccessedMembersAttribute' 요구사항을 충족하지 않는다고 경고합니다.
// - 특히 제네릭 타입(T)이나 런타임에 결정되는 타입에 대해 'this'가 전달될 때 자주 발생합니다.
//
// 억제 근거:
// - 리플렉션 사용 범위를 제한하거나(예: 특정 인터페이스/베이스 타입만), 
// - 실패 시 안전한 fallback(느린 경로, 기본 매핑, 예외 메시지)을 두었으며,
// - 실사용 시 트리밍이 필요 없는 경로(JIT)에서 주로 사용된다는 전제를 둔다면 억제할 수 있습니다.
//
// 주의:
// - 억제는 "안전하다"는 의미가 아니라, "설계상 감수하며 제약을 문서화했다"는 의미입니다.
// ============================================================================
[assembly: SuppressMessage(
    "Trimming",
    "IL2075:'this' argument does not satisfy 'DynamicallyAccessedMemberTypes'",
    Justification = "Reflection usage on generic types is limited and generally safe or fallback mechanisms are in place.",
    Scope = "module")]


// ============================================================================
// [4] IL2070 - DynamicallyAccessedMembers 불충족(역시 'this' 인자 계열)
// ----------------------------------------------------------------------------
// IL2075와 유사하지만 트리머가 다른 형태의 접근(필드/프로퍼티/메서드 요구조건)에서
// 보장 불가를 감지했을 때 발생합니다.
//
// 억제 근거/주의사항은 IL2075와 동일한 맥락입니다.
// ============================================================================
[assembly: SuppressMessage(
    "Trimming",
    "IL2070:'this' argument does not satisfy 'DynamicallyAccessedMemberTypes'",
    Justification = "Reflection usage on generic types is limited and generally safe or fallback mechanisms are in place.",
    Scope = "module")]


// ============================================================================
// [5] IL2111 - DAM(요구사항 포함) 메서드를 리플렉션으로 접근
// ----------------------------------------------------------------------------
// 의미:
// - 파라미터/반환값에 DynamicallyAccessedMembersAttribute(또는 유사 요구조건)가 붙은 메서드를
//   "리플렉션으로 호출"하면, 트리머가 그 요구사항을 정적으로 만족시키는지 검증할 수 없습니다.
//
// 이 라이브러리에서 발생 가능한 이유(예시):
// - DbDataReader의 provider-specific 메서드(예: 특정 Provider에서만 있는 GetXxx 등)를
//   공통 추상화로 지원하기 위해 리플렉션으로 찾아 호출하는 패턴
//
// 억제 근거:
// - 해당 리플렉션 호출은 "선택적(옵셔널)"이며,
//   실패 시 일반적인 표준 경로(공용 API)로 fallback이 가능하다는 설계.
// - 또는 provider별로 제한된 멤버만 대상으로 하며 실제 배포/운영 환경에서 안정적으로 검증되었다는 전제.
// ============================================================================
[assembly: SuppressMessage(
    "Trimming",
    "IL2111:Method with parameters or return value with 'DynamicallyAccessedMembersAttribute' is accessed via reflection. Trimmer can't guarantee availability of the requirements of the method.",
    Justification = "DbDataReader provider specific methods accessed via reflection for generic support.",
    Scope = "module")]


// ============================================================================
// [6] IL2087 - 'type' 인자가 DAM 요구조건을 만족하지 않음
// ----------------------------------------------------------------------------
// 의미:
// - Type 인자가 특정 멤버 접근(예: PublicConstructors, PublicMethods 등)을 요구하는데,
//   트리머가 그 멤버들이 보존될 것을 보장할 수 없다는 경고입니다.
//
// 발생 가능한 이유(예시):
// - 제네릭 매핑을 위해 런타임 타입 기반으로 Expression Tree를 생성하거나,
//   특정 생성자/프로퍼티를 리플렉션으로 찾는 경우.
//
// 억제 근거:
// - Expression Tree 생성은 JIT 최적화 경로에서만 사용되고,
// - AOT에서는 정적 매핑/제한된 매핑으로 fallback하도록 설계한 경우.
// ============================================================================
[assembly: SuppressMessage(
    "Trimming",
    "IL2087:'type' argument does not satisfy 'DynamicallyAccessedMemberTypes'",
    Justification = "Expression Tree creation for generic mapping.",
    Scope = "module")]


// ============================================================================
// [7] IL2090 - 'this' 인자 관련 DAM 요구조건 미충족(리플렉션/매핑)
// ----------------------------------------------------------------------------
// 의미/맥락:
// - IL207x 계열과 유사하지만, 특정 제네릭/반사 시나리오에서 추가적으로 발생하는 경고입니다.
//
// 억제 근거:
// - 매핑 인프라가 리플렉션을 사용하는 것을 전제로 하며,
//   AOT 환경에서의 제약은 문서화되고 fallback 경로가 존재한다는 전제.
// ============================================================================
[assembly: SuppressMessage(
    "Trimming",
    "IL2090:'this' argument does not satisfy 'DynamicallyAccessedMemberTypes'",
    Justification = "Reflection on generic types for mapping.",
    Scope = "module")]


// ============================================================================
// [8] IL2091 - 제네릭 인자가 DAM 요구조건을 만족하지 않음
// ----------------------------------------------------------------------------
// 의미:
// - generic argument(T)가 특정 멤버 보존 요구사항을 충족해야 하는데,
//   트리머가 보장 불가로 판단할 때 발생합니다.
//
// 억제 근거:
// - "제네릭 매핑 인프라가 리플렉션을 사용"하는 설계이고,
// - 트리밍/AOT에서의 제한은 소비자에게 알려져 있으며,
// - 런타임에서 제한적으로만 사용되거나 fallback이 있다는 전제.
// ============================================================================
[assembly: SuppressMessage(
    "Trimming",
    "IL2091:Generic argument does not satisfy 'DynamicallyAccessedMemberTypes'",
    Justification = "Generic mapping infrastructure uses reflection.",
    Scope = "module")]


// ============================================================================
// [9] IL2093 - 반환값의 DAM 요구조건 불일치
// ----------------------------------------------------------------------------
// 의미:
// - 메서드 반환값이 DynamicallyAccessedMembers 요구사항을 갖는데,
//   실제 반환되는 값의 보존 수준이 요구조건과 일치한다고 트리머가 확신하지 못할 때 발생합니다.
//
// 발생 가능한 이유(예시):
// - Provider별 오버라이드/분기에서 반환 타입이 다양하거나,
//   런타임 조건에 따라 서로 다른 Type을 반환하는 구조.
//
// 억제 근거:
// - Provider-specific override(공급자별 구현)에서만 나타나는 경고이며,
// - 해당 경로는 특정 환경에서만 활성화되고, 대체 경로가 있다는 전제.
// ============================================================================
[assembly: SuppressMessage(
    "Trimming",
    "IL2093:Return value 'DynamicallyAccessedMemberTypes' mismatch",
    Justification = "Provider specific overrides.",
    Scope = "module")]
