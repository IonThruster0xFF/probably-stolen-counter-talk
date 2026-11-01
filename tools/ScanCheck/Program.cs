using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

// Read-only invocation of the installed game's actual scanner; no trust records are touched.
string managed = Path.GetFullPath(args[0]);
string dll = Path.GetFullPath(args[1]);
AssemblyLoadContext.Default.Resolving += (context, name) => {
    string path = Path.Combine(managed, name.Name + ".dll");
    return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(managed, "Assembly-CSharp.dll"));
var type = assembly.GetType("ModScanner", true);
// Outside Unity the loader has not populated its game-assembly registry. Supply exactly
// the assemblies physically present in Managed as allowed references for this offline check.
var references = Directory.GetFiles(managed, "*.dll").Select(Path.GetFileNameWithoutExtension).Append("CounterTalk").ToArray();
var scan = type.GetMethod("Scan");
var parameters = scan.GetParameters();
var scanArgs = parameters.Length == 4
    ? new object[] { new[] { dll }, references, Path.GetDirectoryName(dll), true }
    : new object[] { new[] { dll }, references };
var result = scan.Invoke(null, scanArgs);
foreach (var field in result.GetType().GetFields()) {
    var value = field.GetValue(result);
    Console.Write(field.Name + ": ");
    if (value is IEnumerable list && value is not string) {
        foreach (var entry in list) Console.Write(entry + " | ");
        Console.WriteLine();
    } else Console.WriteLine(value);
}
bool refused = (bool)result.GetType().GetProperty("Refused").GetValue(result);
Console.WriteLine("Refused: " + refused);
Environment.ExitCode = refused ? 1 : 0;
