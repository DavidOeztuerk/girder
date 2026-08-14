using Infrastructure.Security.InputSanitization;

namespace Infrastructure.Tests.Security.InputSanitization;

[Trait("Category", "Unit")]
public class InjectionDetectorTests
{
    private readonly InjectionDetector _sut;

    public InjectionDetectorTests()
    {
        _sut = new InjectionDetector(Enumerable.Empty<ICustomInjectionPatterns>());
    }

    #region DetectInjection — safe input

    [Fact]
    public void DetectInjection_SafeInput_NotDetected()
    {
        var result = _sut.DetectInjection("Hello World");

        result.InjectionDetected.Should().BeFalse();
    }

    [Fact]
    public void DetectInjection_NullOrEmpty_NotDetected()
    {
        _sut.DetectInjection(null!).InjectionDetected.Should().BeFalse();
        _sut.DetectInjection("").InjectionDetected.Should().BeFalse();
    }

    #endregion

    #region DetectInjection — SQL

    [Fact]
    public void DetectInjection_SqlKeyword_Detected()
    {
        var result = _sut.DetectInjection("1 UNION SELECT * FROM users");

        result.InjectionDetected.Should().BeTrue();
    }

    [Fact]
    public void DetectInjection_SqlDashDash_Detected()
    {
        var result = _sut.DetectInjection("'; DROP TABLE users--");

        result.InjectionDetected.Should().BeTrue();
    }

    #endregion

    #region DetectInjection — XSS

    [Fact]
    public void DetectInjection_ScriptTag_Detected()
    {
        var result = _sut.DetectInjection("<script>alert('xss')</script>");

        result.InjectionDetected.Should().BeTrue();
    }

    [Fact]
    public void DetectInjection_JavascriptProtocol_Detected()
    {
        var result = _sut.DetectInjection("javascript:alert(1)");

        result.InjectionDetected.Should().BeTrue();
    }

    [Fact]
    public void DetectInjection_EventHandler_Detected()
    {
        var result = _sut.DetectInjection("<img onerror=alert(1)>");

        result.InjectionDetected.Should().BeTrue();
    }

    #endregion

    #region DetectInjection — Path Traversal

    [Fact]
    public void DetectInjection_DotDotSlash_Detected()
    {
        var result = _sut.DetectInjection("../../etc/passwd");

        result.InjectionDetected.Should().BeTrue();
        result.InjectionType.Should().Be(InjectionType.PathTraversal);
    }

    [Fact]
    public void DetectInjection_UrlEncodedTraversal_Detected()
    {
        var result = _sut.DetectInjection("%2e%2e/etc/passwd");

        result.InjectionDetected.Should().BeTrue();
    }

    #endregion

    #region DetectInjection — Command

    [Fact]
    public void DetectInjection_ShellPipe_Detected()
    {
        var result = _sut.DetectInjection("file.txt | rm -rf /");

        result.InjectionDetected.Should().BeTrue();
    }

    [Fact]
    public void DetectInjection_BacktickExec_Detected()
    {
        var result = _sut.DetectInjection("`id`");

        result.InjectionDetected.Should().BeTrue();
    }

    #endregion

    #region DetectInjection — LDAP

    [Fact]
    public void DetectInjection_LdapSpecialChars_Detected()
    {
        // LDAP pattern matches parentheses and asterisks used in LDAP filter injection
        var result = _sut.DetectInjection("(|(uid=*)(cn=admin))");

        result.InjectionDetected.Should().BeTrue();
    }

    #endregion

    #region DetectInjection — result structure

    [Fact]
    public void DetectInjection_WhenDetected_DetectedPatternsNotEmpty()
    {
        var result = _sut.DetectInjection("<script>xss</script>");

        result.DetectedPatterns.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DetectInjection_WhenDetected_RiskLevelNotNone()
    {
        var result = _sut.DetectInjection("../../etc/passwd");

        result.RiskLevel.Should().NotBe(RiskLevel.None);
    }

    [Fact]
    public void DetectInjection_WhenDetected_ConfidenceScorePositive()
    {
        var result = _sut.DetectInjection("1 UNION SELECT * FROM users");

        result.ConfidenceScore.Should().BeGreaterThan(0);
    }

    #endregion

    #region AddCustomPatterns

    [Fact]
    public void AddCustomPatterns_NewType_PatternDetectedAfterwards()
    {
        _sut.AddCustomPatterns(InjectionType.HeaderInjection, @"\bEVIL\b");

        var result = _sut.DetectInjection("EVIL payload");

        result.InjectionDetected.Should().BeTrue();
    }

    [Fact]
    public void AddCustomPatterns_ExistingType_AppendsPattern()
    {
        var before = _sut.GetAllPatterns()[InjectionType.PathTraversal].Count;

        _sut.AddCustomPatterns(InjectionType.PathTraversal, @"custom-traversal");

        _sut.GetAllPatterns()[InjectionType.PathTraversal].Count.Should().BeGreaterThan(before);
    }

    #endregion

    #region GetAllPatterns

    [Fact]
    public void GetAllPatterns_ReturnsAllDefaultTypes()
    {
        var patterns = _sut.GetAllPatterns();

        patterns.Keys.Should().Contain(InjectionType.SqlInjection);
        patterns.Keys.Should().Contain(InjectionType.XssInjection);
        patterns.Keys.Should().Contain(InjectionType.CommandInjection);
        patterns.Keys.Should().Contain(InjectionType.PathTraversal);
        patterns.Keys.Should().Contain(InjectionType.LdapInjection);
        patterns.Keys.Should().Contain(InjectionType.XPathInjection);
    }

    [Fact]
    public void GetAllPatterns_ReturnsSnapshot_NotLiveReference()
    {
        var patterns1 = _sut.GetAllPatterns();
        var patterns2 = _sut.GetAllPatterns();

        patterns1.Should().NotBeSameAs(patterns2);
    }

    #endregion

    #region CustomInjectionPatterns wired at construction

    [Fact]
    public void Constructor_WithCustomPatterns_DetectsCustomInjection()
    {
        var customPattern = new CustomInjectionPatterns(InjectionType.HeaderInjection, new[] { @"\bSECRET\b" });
        var detector = new InjectionDetector(new[] { customPattern });

        var result = detector.DetectInjection("this is SECRET");

        result.InjectionDetected.Should().BeTrue();
    }

    #endregion
}
