using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;

internal static partial class Program
{
    static Version ProductVersion(string assemblyPath)
    {
        var identity=AssemblyName.GetAssemblyName(Path.GetFullPath(assemblyPath));
        if(identity.Name!="WorkIsland" || identity.Version is null)
            throw new ArgumentException("Supply the actual published WorkIsland.dll, not the evidence generator.");
        return identity.Version;
    }

    static void SheetEvidence(string output,string product,IEnumerable<string> inputs)
    {
        string Hash(string file)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        File.WriteAllText(output+".json",JsonSerializer.Serialize(new
        {
            productAssembly=Path.GetFullPath(product),productVersion=ProductVersion(product).ToString(),
            productSha256=Hash(product),generatorVersion=typeof(Program).Assembly.GetName().Version!.ToString(),
            output=Path.GetFullPath(output),outputSha256=Hash(output),
            inputs=inputs.Select(file=>new{file=Path.GetFullPath(file),sha256=Hash(file)}).ToArray(),
            method="Original capture pixels composed without retouching; heading comes from the explicitly supplied product DLL. Input hashes bind the sheet to its source images; this metadata is not a new capture or a mouse-input test."
        },new JsonSerializerOptions{WriteIndented=true}));
    }
}
