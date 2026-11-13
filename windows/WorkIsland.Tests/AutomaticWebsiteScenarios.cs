using System.Text.Json;
using WorkIsland.Core;

internal static class AutomaticWebsiteScenarios
{
    internal static void Run(Action<string,Action> test)
    {
        void Require(bool value){if(!value)throw new Exception("Automatic website invariant failed");}
        foreach(var (address,keyword) in new (string,string?)[]{
            ("https://docs.example.com/path","example"),("https://app.example.com/","example"),
            ("https://sub.example.co.uk/path","example"),("https://www.example.com.cn/","example"),
            ("https://sub.example.gov.au/","example"),("https://www.example.ac.jp/","example"),
            ("https://EXAMPLE.COM./","example"),("https://foo.bar.example/","bar"),
            ("https://127.0.0.1/path",null),("https://[::1]/",null),("http://localhost/path",null),
            ("https://co.uk/",null),("file:///tmp/private",null),("chrome://settings/",null),
            ("example.com",null),("https://example.com/\n",null)})
            test("Mac automatic keyword: "+address.Replace("\n","[control]"),()=>
            {
                var rule=RecognitionRules.AutomaticWebsite(address);Require(rule?.PathContains==keyword);
                if(keyword is not null)Require(rule is {IsEnabled:true,MatchesHostOnly:true}&&rule.Identifier==RecognitionRules.Website(keyword)!.Identifier);
            });
        test("automatic long and international addresses retain stable keyword-only identity",()=>
        {
            var rule=RecognitionRules.AutomaticWebsite("https://user:fixturepassword@docs.example.com/path?q="+new string('x',4096)+"#fixturefragment");
            Require(rule?.Identifier==RecognitionRules.AutomaticWebsite("https://app.example.com/")?.Identifier);
            var json=JsonSerializer.Serialize(rule);Require(!json.Contains("fixturepassword")&&!json.Contains("fixturefragment")&&!json.Contains("docs.example"));
            Require(RecognitionRules.AutomaticWebsite("https://子域.例子.测试/")?.PathContains=="xn--fsqu00a");
        });
        test("new keyword uses whole host label matching without altering old explicit host scope",()=>
        {
            var added=RecognitionRules.AutomaticWebsite("https://docs.example.com/")!;var old=RecognitionRules.Website("docs.example.com")!;
            Require(ActivityDecision.WebsiteMatches("https://app.example.com/",added)&&ActivityDecision.WebsiteMatches("https://example.co.uk/",added));
            Require(!ActivityDecision.WebsiteMatches("https://notexample.com/",added)&&!ActivityDecision.WebsiteMatches("https://other.test/example",added));
            Require(!ActivityDecision.WebsiteMatches("https://app.example.com/",old)&&ActivityDecision.WebsiteMatches("https://docs.example.com/",old));
        });
        test("semantic keyword equality follows Mac title fallback trimming and case independently of ID",()=>
        {
            var old=new WebsiteRule("custom-id","Custom title","  EXAMPLE ","",false,null,"study");
            Require(RecognitionRules.SameWebsiteKeyword(old,"example")&&RecognitionRules.SameWebsiteKeyword(old with{PathContains="ExAmPlE"}," example "));
            Require(!RecognitionRules.SameWebsiteKeyword(old with{PathContains="docs.example.com"},"example"));
            Require(old.Identifier=="custom-id"&&!old.IsEnabled&&old.MatchesHostOnly is null);
        });
    }
}
