using TianMu.Modules.Notify.Notifications;

namespace TianMu.Tests;

public class ToastPayloadTests
{
    [Fact]
    public void Reads_title_body_and_attribution()
    {
        var parsed = ToastPayload.Parse("""
            <toast launch="x"><visual><binding template="ToastGeneric">
              <text>林道友</text>
              <text>今晚的組會改到八點</text>
              <text>記得帶資料</text>
              <text placement="attribution">mail.google.com</text>
            </binding></visual></toast>
            """);

        Assert.NotNull(parsed);
        Assert.Equal("林道友", parsed!.Value.Title);
        Assert.Equal("今晚的組會改到八點\n記得帶資料", parsed.Value.Body);
        Assert.Equal("mail.google.com", parsed.Value.Attribution);
    }

    [Theory]
    [InlineData("<toast><visual><binding template=\"ToastGeneric\"></binding></visual></toast>")]
    [InlineData("not xml")]
    [InlineData("<badge value=\"3\"/>")]
    public void Unusable_payloads_return_null(string xml) => Assert.Null(ToastPayload.Parse(xml));
}

public class OtpExtractorTests
{
    [Theory]
    [InlineData("您的验证码是 482913，5分钟内有效", "482913")]
    [InlineData("【玉山銀行】驗證碼：123-456，請勿告訴他人", "123456")]
    [InlineData("Verification code: 482913", "482913")]
    [InlineData("G-482913 is your Google verification code.", "482913")]
    [InlineData("Your sign-in code is 9921 0384", "99210384")]
    [InlineData("[GitHub] Please verify your device\nVerification code: 77120", "77120")]
    [InlineData("認証コード 551234", "551234")]
    public void Finds_codes(string text, string expected) => Assert.Equal(expected, OtpExtractor.Find(text));

    [Theory]
    [InlineData("訂單 20261105 已出貨")]                     // 沒有關鍵字
    [InlineData("您的验证码将在 10 分钟后失效")]                 // 只有時間
    [InlineData("驗證碼將於 2026 年起改版")]                    // 年份
    [InlineData("本月帳單 $1234 已出帳")]                       // 金額、沒有關鍵字
    [InlineData("")]
    public void Ignores_non_codes(string text) => Assert.Null(OtpExtractor.Find(text));

    [Theory]
    [InlineData("482913", "482 913")]
    [InlineData("99210384", "9921 0384")]
    [InlineData("77120", "77120")]
    public void Pretty_groups_digits(string code, string expected) => Assert.Equal(expected, OtpExtractor.Pretty(code));
}

public class AppNameTests
{
    [Theory]
    [InlineData("Microsoft.Office.OUTLOOK.EXE.15", "Outlook")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("Microsoft.YourPhone_8wekyb3d8bbwe!App", "手機連結")]
    [InlineData("Some.Vendor.MyTool.exe", "MyTool")]
    [InlineData("", "通知")]
    public void Guess_names(string aumid, string expected) =>
        Assert.Equal(expected, aumid.Length == 0 ? AppNames.Resolve(aumid) : AppNames.Guess(aumid));
}
