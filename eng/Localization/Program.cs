using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var strings = new SortedSet<string>(StringComparer.Ordinal);
var files = Directory.GetFiles(Path.Combine(root, "v2rayN/ServiceLib/Trisko"), "*.cs")
    .Concat(Directory.GetFiles(Path.Combine(root, "v2rayN/v2rayN.Desktop"), "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
            && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)));
foreach (var file in files.Where(f => !f.Contains("TriskoLocalization")))
{
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(file));
    var rewrite = new Localizer(strings);
    var result = rewrite.Visit(tree.GetRoot());
    if (args.Contains("--write")) File.WriteAllText(file, result.ToFullString());
}
File.WriteAllText(Path.Combine(root, "source-strings.json"), JsonSerializer.Serialize(strings, new JsonSerializerOptions { WriteIndented=true, Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine($"{strings.Count} strings");

class Localizer(SortedSet<string> strings) : CSharpSyntaxRewriter
{
    static bool Russian(string s) => Regex.IsMatch(s, "[А-Яа-яЁё]");
    static ExpressionSyntax Call(string method, string key, IEnumerable<string> values) => SyntaxFactory.ParseExpression(
        $"TriskoLocalization.{method}(" +
        SyntaxFactory.Literal(key).ToFullString() + string.Concat(values.Select(v => ", " + v)) + ")");
    public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
    {
        if (!node.IsKind(SyntaxKind.StringLiteralExpression) || !Russian(node.Token.ValueText)) return base.VisitLiteralExpression(node);
        if (node.Ancestors().OfType<InvocationExpressionSyntax>().Any(n => n.Expression.ToString().Contains("TriskoLocalization")))
        {
            strings.Add(node.Token.ValueText);
            return node;
        }
        if (node.Ancestors().Any(n => n is AttributeSyntax || n is CaseSwitchLabelSyntax || n is ConstantPatternSyntax || n is ParameterSyntax
            || n is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.ConstKeyword))) return node;
        strings.Add(node.Token.ValueText);
        return Call("Text", node.Token.ValueText, []).WithTriviaFrom(node);
    }
    public override SyntaxNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
    {
        if (!Russian(node.ToString())) return base.VisitInterpolatedStringExpression(node);
        var key = ""; var values = new List<string>();
        foreach (var part in node.Contents)
        {
            if (part is InterpolatedStringTextSyntax text) key += text.TextToken.ValueText;
            else if (part is InterpolationSyntax hole)
            {
                key += "{" + values.Count + hole.AlignmentClause?.ToString() + hole.FormatClause?.ToString() + "}";
                values.Add(Visit(hole.Expression).ToFullString());
            }
        }
        strings.Add(key);
        return Call("Format", key, values).WithTriviaFrom(node);
    }
}
