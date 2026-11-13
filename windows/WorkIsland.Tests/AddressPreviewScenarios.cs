using System.Globalization;
using System.Text;
using WorkIsland.Core;

internal static class AddressPreviewScenarios
{
    internal static void Run(Action<string,Action> test)
    {
        void Require(bool value){if(!value)throw new Exception("Address preview invariant failed");}
        test("Mac address preview removes credentials query and fragment before display",()=>
            Require(RecognitionRules.AddressPreview("https://user:password@example.com/path?token=secret#key")=="https://example.com/path"));
        test("long credentials are removed before a preview can be shortened",()=>
        {
            var value="https://fixtureuser:"+new string('p',300)+"@docs.example.com/path?fixturetoken="+new string('q',300)+"#fixturefragment";
            Require(RecognitionRules.AddressPreview(value)=="https://docs.example.com/path");
        });
        test("default preview uses 52 text elements then a single ellipsis",()=>
        {
            var safe="https://example.com/"+new string('a',100);var preview=RecognitionRules.AddressPreview(safe);
            Require(preview==safe[..52]+"…"&&new StringInfo(preview!).LengthInTextElements==53);
        });
        test("exact preview boundary is not truncated",()=>
        {
            var safe="https://example.com/"+new string('a',32);Require(safe.Length==52&&RecognitionRules.AddressPreview(safe)==safe);
            Require(RecognitionRules.AddressPreview(safe+"a")==safe+"…");
        });
        test("zero and negative preview limits follow Mac empty-prefix behavior",()=>
            Require(RecognitionRules.AddressPreview("https://example.com/",0)=="…"&&RecognitionRules.AddressPreview("https://example.com/",-3)=="…"));
        test("query length does not affect a short safe preview",()=>
            Require(RecognitionRules.AddressPreview("https://example.com/p?q="+new string('x',4096)+"#token")=="https://example.com/p"));
        test("preview supports HTTP hosts independently of automatic keyword eligibility",()=>
        {
            Require(RecognitionRules.AddressPreview("http://localhost:8080/path?q=x")=="http://localhost:8080/path");
            Require(RecognitionRules.AddressPreview("http://u:p@[::1]:8080/path?q=x#y")=="http://[::1]:8080/path");
            Require(RecognitionRules.AutomaticWebsite("http://localhost:8080/path") is null);
        });
        test("preview rejects non-web relative missing and control-bearing addresses",()=>
        {
            foreach(var value in new string?[]{null,"","example.com","file:///tmp/private","chrome://settings","data:text/plain,secret","https://example.com/?q=\n"})Require(RecognitionRules.AddressPreview(value) is null);
        });
        test("different pages keep recognizable safe previews while sharing a keyword",()=>
        {
            var one="https://docs.example.com/project?a=b";var two="https://app.example.com/drafts#c";
            Require(RecognitionRules.AddressPreview(one)!=RecognitionRules.AddressPreview(two)&&RecognitionRules.AutomaticWebsite(one)?.Identifier==RecognitionRules.AutomaticWebsite(two)?.Identifier);
        });
        test("international preview remains well-formed when shortened",()=>
        {
            var preview=RecognitionRules.AddressPreview("https://例子.测试/资料/👩‍💻/e\u0301/"+new string('x',100),32)!;
            _=new UTF8Encoding(false,true).GetBytes(preview);Require(new StringInfo(preview).LengthInTextElements==33&&preview.EndsWith('…'));
        });
        test("existing safe-address utility keeps its complete sanitized path",()=>
        {
            var path=new string('x',100);Require(RecognitionRules.SafeAddress("https://u:p@example.com/"+path+"?token=x#y")=="https://example.com/"+path);
        });
    }
}
