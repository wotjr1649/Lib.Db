// ============================================================================
// File : Lib.Db.Verification.Tests/Helpers/TestOptionsFactory.cs
// Role : ?ŒìŠ¤?¸ìš© LibDbOptions ?ì„± ?¬í¼ (ìµœì†Œ ? íš¨ ?¤ì •)
// Env  : .NET 10 / C# 14
// Notes:
//   - ConnectionStrings ?„ë½?¼ë¡œ ?¸í•œ OptionsValidationException ë°©ì?
//   - ?ŒìŠ¤?¸ë³„ ì»¤ìŠ¤?°ë§ˆ?´ì§• ì§€??(WithOverrides)
// ============================================================================

#nullable enable

using Lib.Db.Configuration;

namespace Lib.Db.Verification.Tests.Helpers;

/// <summary>
/// ?ŒìŠ¤?¸ìš© ? íš¨??LibDbOptions ?¸ìŠ¤?´ìŠ¤ë¥??ì„±?˜ëŠ” ?•ì  ?©í† ë¦??´ë˜?¤ì…?ˆë‹¤.
/// ConnectionStrings ???„ìˆ˜ ?¤ì •???¬í•¨?˜ì—¬ OptionsValidationException??ë°©ì??©ë‹ˆ??
/// </summary>
public static class TestOptionsFactory
{
    /// <summary>
    /// ìµœì†Œ?œì˜ ? íš¨???¤ì •??ê°€ì§?LibDbOptionsë¥??ì„±?©ë‹ˆ??
    /// </summary>
    /// <returns>?ŒìŠ¤?¸ìš© LibDbOptions ?¸ìŠ¤?´ìŠ¤</returns>
    public static LibDbOptions CreateValidOptions()
    {
        return new LibDbOptions
        {
            ConnectionStrings = new Dictionary<string, string>
            {
                ["Default"] = "Server=localhost;Database=LibDbVerificationTest;Integrated Security=true;TrustServerCertificate=true;",
                ["Admin"] = "Server=localhost;Database=LibDbVerificationTest;Integrated Security=true;TrustServerCertificate=true;"
            },
            EnableSharedMemoryCache = false // ê¸°ë³¸ ?ŒìŠ¤???˜ê²½?ì„œ??ë¹„í™œ?±í™”
        };
    }

    /// <summary>
    /// ? íš¨??ê¸°ë³¸ ?µì…˜???ì„±?˜ê³ , ?¬ìš©???•ì˜ ?¤ì •???¤ë²„?¼ì´?œí•©?ˆë‹¤.
    /// </summary>
    /// <param name="configure">?¤ì • ?¤ë²„?¼ì´???¡ì…˜</param>
    /// <returns>ì»¤ìŠ¤?°ë§ˆ?´ì§•??LibDbOptions ?¸ìŠ¤?´ìŠ¤</returns>
    public static LibDbOptions CreateValidWithOverrides(Action<LibDbOptions> configure)
    {
        var options = CreateValidOptions();
        configure(options);
        return options;
    }

    /// <summary>
    /// ConnectionStringsë§??¬í•¨??ìµœì†Œ ?µì…˜ (?¤ë¥¸ ?¤ì • ?†ìŒ)
    /// </summary>
    public static LibDbOptions CreateMinimal()
    {
        return new LibDbOptions
        {
            ConnectionStrings = new Dictionary<string, string>
            {
                ["Default"] = "Server=localhost;Database=Test;Integrated Security=true;TrustServerCertificate=true;"
            }
        };
    }
}

