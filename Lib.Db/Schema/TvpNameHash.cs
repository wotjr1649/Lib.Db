// ============================================================================
// 파일: Lib.Db/Schema/TvpNameHash.cs
// 설명: TVP 컬럼명 비교용 결정적 FNV-1a 해시
// ============================================================================

#nullable enable

namespace Lib.Db.Schema;

internal static class TvpNameHash
{
    public static int Compute(string name)
    {
        unchecked
        {
            const uint offset = 2166136261u;
            const uint prime = 16777619u;
            uint hash = offset;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if ((uint)(c - 'A') <= 25u)
                    c = (char)(c | 0x20);

                hash ^= c;
                hash *= prime;
            }

            return (int)hash;
        }
    }
}
