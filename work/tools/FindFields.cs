using System; using System.Linq; using System.Text.RegularExpressions; using Mono.Cecil;
class FindFields {
  static void Main(string[] a) {
    var r = new DefaultAssemblyResolver(); r.AddSearchDirectory("/home/wayne/date/icey/out/extracted/v1.1.1/unity_data/assets/bin/Data/Managed");
    var asm = AssemblyDefinition.ReadAssembly(a[0], new ReaderParameters { AssemblyResolver = r });
    string typePat = a.Length > 1 ? a[1] : "Player|Character|Actor|Hero|Entity|Unit";
    string fieldPat = a.Length > 2 ? a[2] : "hp|health|attack|damage|energy|mp|sp|power|max|life|blood";
    var tRe = new Regex(typePat, RegexOptions.IgnoreCase);
    var fRe = new Regex(fieldPat, RegexOptions.IgnoreCase);
    foreach (var t in asm.MainModule.Types) {
      if (!tRe.IsMatch(t.Name)) continue;
      var hits = t.Fields.Where(f => fRe.IsMatch(f.Name) && IsNum(f.FieldType)).ToList();
      if (hits.Count == 0) continue;
      Console.WriteLine($"== {t.FullName}  (base={t.BaseType?.Name})");
      foreach (var f in hits) Console.WriteLine($"     {(f.IsStatic ? "static " : "")}{f.FieldType.Name,-8} {f.Name}");
    }
  }
  static bool IsNum(TypeReference t) { string n = t.Name; return n == "Single" || n == "Int32" || n == "Double" || n == "Int64"; }
}
