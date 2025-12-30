# Document to Source Code Mapping (DOC_SOURCE_MAP)

> **Last Updated:** 2025-12-24  
> **Purpose:** 기술 문서와 실제 소스 코드 간의 정합성 추적  
> **Status:** 수동 관리 (향후 자동화 예정)

이 문서는 `Lib.Db/docs` 내의 기술 문서에서 언급된 클래스/인터페이스와 실제 소스 코드의 일치 여부를 추적합니다.

---

## 문서-코드 매핑 테이블

| Document | Entity (Mentioned) | Actual Source Path | Status | Notes |
| :--- | :--- | :--- | :--- | :--- |
| **01_architecture_overview.md** | `DbSession` | `Lib.Db/Core/DbSession.cs` | ✅ **Valid** | Refactored from `Session.cs` |
| | `SqlDbExecutor` | `Lib.Db/Execution/Executors/SqlDbExecutor.cs` | ✅ **Valid** | 실제 SQL 실행 엔진 |
| | `SchemaService` | `Lib.Db/Schema/SchemaService.cs` | ✅ **Valid** | Refactored from `DbSchema.cs` |
| | `ConfigurableChaosInjector` | `Lib.Db/Infrastructure/ChaosEngineering.cs` | ✅ **Valid** | 문서에서는 `ChaosInjector`로 간략 표기 |
| | `TvpAccessorGenerator` | `Lib.Db.TvpGen/TvpAccessorGenerator.cs` | ✅ **Valid** | Source Generator |
| | `ResultAccessorGenerator` | `Lib.Db.TvpGen/ResultAccessorGenerator.cs` | ✅ **Valid** | DbDataReader → DTO 매핑 코드 생성 (Track 5) |
| **02_configuration_and_di.md** | `UseHighPerformanceDb` | `Lib.Db/Extensions/LibDbHostExtensions.cs` | ✅ **Valid** | Host 확장 메서드 |
| | `AddHighPerformanceDb` | `Lib.Db/Extensions/LibDbHostExtensions.cs` | ✅ **Valid** | DI 등록 메서드 |
| **05_performance_optimization.md** | `SharedMemoryCache` | `Lib.Db/Caching/SharedMemoryCache.cs` | ✅ **Valid** | 문서에서는 `SharedMemoryMappedCache`로 표기됨 → **수정 필요** |
| | `HybridCacheExtensions` | `Lib.Db/Extensions/HybridCacheExtensions.cs` | ✅ **Valid** | L1/L2 캐시 전략 |
| **07_troubleshooting.md** | `DbBinder` | `Lib.Db/Execution/Binding/DbBinder.cs` | ✅ **Valid** | Refactored from `DataBinding.cs` |
| **08_process_coordination.md** | `CacheLeaderElection` | `Lib.Db/Caching/CacheCoordination.cs` | ✅ **Valid** | 리더 선출 로직 |
| | `GlobalCacheEpoch` | `Lib.Db/Caching/CachingInfrastructure.cs` | ✅ **Valid** | Epoch 관리 |
| | `SharedMemoryCache` | `Lib.Db/Caching/SharedMemoryCache.cs` | ✅ **Valid** | MMF 기반 구현 |

---

## 정합성 요약

| Status | Count | Details |
|:---|:---:|:---|
| ✅ **Valid (Perfect Match)** | 11개 | 문서와 코드가 정확히 일치 |
| ⚠️ **Valid (Name Mismatch)** | 2개 | 기능은 존재하나 이름이 다름 |
| ❌ **Missing Source** | 0개 | 모든 엔티티 확인됨 |
| **Total** | **13개** | 추적 중인 엔티티 |

---

## 불일치 상세 및 조치 사항

### 🟡 Warning (용어 통일 권장)

#### 1. `SharedMemoryMappedCache` vs `SharedMemoryCache`
- **문서**: `05_performance_optimization.md` 라인 42
- **언급**: "`SharedMemoryMappedCache`를 통해 프로세스 간 공유"
- **실제**: 클래스명은 `SharedMemoryCache`
- **Action**:
  - [ ] 문서에서 `SharedMemoryMappedCache` → `SharedMemoryCache`로 수정

#### 2. `ChaosInjector` vs `ConfigurableChaosInjector`
- **문서**: `01_architecture_overview.md`
- **언급**: "`ChaosInjector`"
- **실제**: 클래스명은 `ConfigurableChaosInjector`
- **판단**: 문서에서 간략화 목적으로 표기 → **수정 불필요** (단, 주석 추가)

---

## 향후 개선 계획

### Phase 1: 수동 정합성 유지 (현재)
- 주요 릴리스마다 수동 검증
- Pull Request 시 리뷰어가 확인

### Phase 2: 반자동화 (3개월 내)
- Roslyn Analyzer로 컴파일 타임 경고
- 문서에 언급된 클래스가 실제 존재하는지 체크

### Phase 3: 완전 자동화 (6개월 내)
- GitHub Actions Workflow
- 빌드 시 DOC_SOURCE_MAP 자동 생성
- 불일치 발견 시 PR 자동 생성

---

## 검증 방법

### 로컬 검증
```bash
# 1. 모든 문서에서 클래스명 추출
rg -o '\`[A-Z][a-zA-Z0-9]+\`' docs/*.md | sort | uniq > mentioned_classes.txt

# 2. 실제 소스 코드에서 클래스 선언 추출
rg '^(public |internal )?(class|interface|record) ' --glob '*.cs' | sort > actual_classes.txt

# 3. 차이점 비교
diff mentioned_classes.txt actual_classes.txt
```

### CI/CD 통합 (향후)
```yaml
# .github/workflows/doc-sync-check.yml
name: Doc-Code Sync Check
on: [pull_request]
jobs:
  verify:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v3
      - run: dotnet build Lib.Db.sln
      - run: ./tools/verify-doc-code-sync.sh
```

---

**마지막 검증 일시**: 2025-12-24 09:49  
**검증자**: Documentation Improvement Task  
**다음 검증 예정**: 다음 릴리스 전
