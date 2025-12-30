// ============================================================================
// 파일: Lib.Db/GlobalUsings.cs
// 설명: 프로젝트 전역 using 선언
// 목적: 중복 using 제거 및 컴파일 속도 향상
// 대상: .NET 10 / C# 14
// ============================================================================

#region [1. 시스템 핵심 네임스페이스]

global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Text;
global using System.Threading;
global using System.Threading.Tasks;

#endregion

#region [2. 데이터 액세스 네임스페이스]

global using System.Data;
global using System.Data.Common;
global using Microsoft.Data.SqlClient;

#endregion

#region [3. Microsoft Extensions 네임스페이스]

global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Caching.Hybrid;

#endregion

#region [4. Lib.Db 내부 네임스페이스]

global using Lib.Db.Contracts;
global using Lib.Db.Core;
global using Lib.Db.Contracts.Core;
global using Lib.Db.Contracts.Cache;
global using Lib.Db.Contracts.Diagnostics;
global using Lib.Db.Configuration;
global using Lib.Db.Diagnostics;
global using Lib.Db.Repository;

#endregion

#region [5. 타입 별칭 (Type Aliases)]

/// <summary>저장 프로시저 이름을 나타내는 타입 별칭입니다.</summary>
/// <summary>저장 프로시저 이름을 나타내는 타입 별칭입니다.</summary>
global using SpName = Lib.Db.Contracts.Core.DbObjectName<Lib.Db.Contracts.Core.SpTrait>;

/// <summary>TVP(Table-Valued Parameter) 타입 이름을 나타내는 타입 별칭입니다.</summary>
global using TvpName = Lib.Db.Contracts.Core.DbObjectName<Lib.Db.Contracts.Core.TvpTrait>;

#endregion