using System.Text.Json;
using WorkIsland.Core;

internal static class WebsiteAddressScenarios
{
    internal static void Run(Action<string,Action> test)
    {
        void Require(bool value){if(!value)throw new Exception("Website address invariant failed");}
        var tail=new string('x',4096);
        test("automatic capture accepts long path query and fragment while storing only host",()=>
        {
            foreach(var url in new[]{"https://Docs.Example.com/"+tail,"https://Docs.Example.com/?q="+tail,"https://Docs.Example.com/#"+tail})
            {
                var rule=RecognitionRules.Website(url,true);
                Require(rule is {DisplayName:"docs.example.com",PathContains:"docs.example.com",TitleContains:"docs.example.com",MatchesHostOnly:true});
            }
        });
        test("manual pasted long URL shares the same normalized host identity",()=>
        {
            var rule=RecognitionRules.Website("https://Docs.Example.com/search?q="+tail);
            Require(rule?.Identifier==RecognitionRules.Website("docs.example.com")?.Identifier);
        });
        test("long automatic address retains no credentials query or fragment in serialized rule",()=>
        {
            var rule=RecognitionRules.Website("https://fixtureuser:fixturepassword@docs.example.com/path?fixturetoken="+tail+"#fixturefragment",true);
            Require(rule is not null);var text=JsonSerializer.Serialize(rule,AtomicStore.JsonOptions);
            Require(!text.Contains("fixtureuser")&&!text.Contains("fixturepassword")&&!text.Contains("fixturetoken")&&!text.Contains("fixturefragment")&&!text.Contains(tail));
        });
        test("long international address normalizes using the same IDN host as its short address",()=>
        {
            var shortRule=RecognitionRules.Website("https://例子.测试/",true);
            var longRule=RecognitionRules.Website("https://例子.测试/?q="+tail,true);
            Require(shortRule is not null&&longRule?.Identifier==shortRule.Identifier&&longRule.PathContains=="xn--fsqu00a.xn--0zwm56d");
        });
        test("website limit applies to normalized host and still rejects oversized keywords",()=>
        {
            var host=string.Join(".",new string('a',63),new string('b',63),new string('c',63),new string('d',61));
            Require(host.Length==253&&RecognitionRules.Website(host) is not null);
            Require(RecognitionRules.Website(host+"d") is null&&RecognitionRules.Website(new string('a',254)) is null);
        });
        test("long malformed or internal browser addresses remain rejected",()=>
        {
            foreach(var value in new[]{"chrome://settings/"+tail,"file:///"+tail,"https://example.com/?q=\n"+tail,"not an address "+tail})
                Require(RecognitionRules.Website(value,true) is null);
        });
        test("long address capture matches the same website and does not create a new rule key",()=>
        {
            var url="https://docs.example.com/path?q="+tail;
            var rule=RecognitionRules.Website(url,true);var shortRule=RecognitionRules.Website("https://docs.example.com/",true);
            Require(rule is not null&&shortRule is not null&&rule.Identifier==shortRule.Identifier&&ActivityDecision.WebsiteMatches(url,rule));
            Require(!ActivityDecision.WebsiteMatches("https://docs.example.com.evil.test/",rule!));
        });
    }
}
