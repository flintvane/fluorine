using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Fluorine.Generators
{
    [Generator]
    public sealed class ReactivePropertyGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var reactive = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "Fluorine.ReactiveAttribute",
                    static (node, _) => node is PropertyDeclarationSyntax,
                    static (ctx, _) => ReactiveModel.Create(ctx))
                .Where(static model => model != null);

            var auto = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    "Fluorine.AutoNotifyAttribute",
                    static (node, _) => node is PropertyDeclarationSyntax,
                    static (ctx, _) => ReactiveModel.Create(ctx))
                .Where(static model => model != null);

            var combined = reactive.Collect().Combine(auto.Collect());
            context.RegisterSourceOutput(combined, static (spc, pair) => Emit(spc, pair.Left, pair.Right));
        }

        static void Emit(
            SourceProductionContext spc,
            ImmutableArray<ReactiveModel> reactive,
            ImmutableArray<ReactiveModel> auto)
        {
            var byType = new Dictionary<string, List<ReactiveModel>>(StringComparer.Ordinal);
            Add(byType, reactive);
            Add(byType, auto);

            foreach (var pair in byType)
            {
                var models = pair.Value;
                models.Sort(static (a, b) => a.SpanStart.CompareTo(b.SpanStart));

                ReactiveModel shell = null;
                var generated = new List<ReactiveModel>();
                foreach (var model in models)
                {
                    if (model.DiagnosticId != null)
                    {
                        spc.ReportDiagnostic(model.ToDiagnostic());
                        continue;
                    }

                    if (shell == null)
                        shell = model;
                    generated.Add(model);
                }

                if (shell == null || generated.Count == 0)
                    continue;

                spc.AddSource(shell.HintName, shell.Render(generated));
            }
        }

        static void Add(Dictionary<string, List<ReactiveModel>> byType, ImmutableArray<ReactiveModel> models)
        {
            if (models.IsDefaultOrEmpty)
                return;
            foreach (var model in models)
            {
                if (model == null)
                    continue;
                List<ReactiveModel> list;
                if (!byType.TryGetValue(model.TypeKey, out list))
                {
                    list = new List<ReactiveModel>();
                    byType.Add(model.TypeKey, list);
                }

                var duplicate = false;
                foreach (var existing in list)
                {
                    if (existing.Path == model.Path && existing.SpanStart == model.SpanStart)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (!duplicate)
                    list.Add(model);
            }
        }
    }

    sealed class ReactiveModel
    {
        public string TypeKey;
        public string HintName;
        public string Open;
        public string Close;
        public string MemberIndent;
        public string PropertyName;
        public string PreferredField;
        public string PropertyType;
        public string Modifiers;
        public string GetterPrefix;
        public string SetterPrefix;
        public string SetterKeyword;
        public bool EmitAutoNotify;
        public string ExistingMembers;
        public string DiagnosticId;
        public string DiagnosticMessage;
        public string Path;
        public int SpanStart;
        public int SpanLength;
        public int StartLine;
        public int StartColumn;
        public int EndLine;
        public int EndColumn;

        public static ReactiveModel Create(GeneratorAttributeSyntaxContext context)
        {
            var syntax = context.TargetNode as PropertyDeclarationSyntax;
            var property = context.TargetSymbol as IPropertySymbol;
            if (syntax == null || property == null)
                return null;

            var reactive = HasAttribute(property, "Fluorine.ReactiveAttribute");
            var auto = HasAttribute(property, "Fluorine.AutoNotifyAttribute");
            if (!reactive && !auto)
                return null;

            AccessorDeclarationSyntax getter;
            AccessorDeclarationSyntax setter;
            if (!IsOpenGetSet(syntax, out getter, out setter))
            {
                if (!reactive)
                    return null;
                return Fail(property, syntax, Ids.Shape, "get and set (or init) accessors with no body");
            }

            if (!HasModifier(syntax.Modifiers, SyntaxKind.PartialKeyword))
            {
                if (!reactive)
                    return null;
                return Fail(property, syntax, Ids.Partial, "be partial");
            }

            if (property.IsStatic)
                return Fail(property, syntax, Ids.Static, "be an instance property");
            if (property.IsAbstract || property.IsIndexer || property.RefKind != RefKind.None
                || property.ExplicitInterfaceImplementations.Length != 0)
                return Fail(property, syntax, Ids.Shape, "be an ordinary instance property");

            var type = property.ContainingType;
            if (type == null || type.TypeKind == TypeKind.Interface || type.IsFileLocal)
                return Fail(property, syntax, Ids.Type, "be a partial class, record or struct");
            if (!TypeIsPartial(type))
                return Fail(property, syntax, Ids.Type, "be partial");

            string open, close, indent;
            BuildShell(type, out open, out close, out indent);

            return new ReactiveModel
            {
                TypeKey = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                HintName = MakeHintName(type),
                Open = open,
                Close = close,
                MemberIndent = indent,
                PropertyName = property.Name,
                PreferredField = FieldName(property.Name),
                PropertyType = property.Type.ToDisplayString(TypeFormat),
                Modifiers = ModifiersExceptPartial(syntax.Modifiers),
                GetterPrefix = Prefix(getter),
                SetterPrefix = Prefix(setter),
                SetterKeyword = setter.Keyword.ValueText,
                EmitAutoNotify = !auto,
                ExistingMembers = Members(type),
                Path = syntax.SyntaxTree.FilePath ?? "",
                SpanStart = syntax.SpanStart,
                SpanLength = 0
            };
        }

        public Diagnostic ToDiagnostic()
        {
            var descriptor = DiagnosticId == Ids.Partial ? MustBePartial
                : DiagnosticId == Ids.Type ? TypeMustBePartial
                : DiagnosticId == Ids.Static ? MustBeInstance
                : MustHaveAccessors;

            Location location;
            if (string.IsNullOrEmpty(Path))
                location = Location.None;
            else
            {
                location = Location.Create(
                    Path,
                    new TextSpan(SpanStart, SpanLength),
                    new LinePositionSpan(
                        new LinePosition(StartLine, StartColumn),
                        new LinePosition(EndLine, EndColumn)));
            }

            return Microsoft.CodeAnalysis.Diagnostic.Create(descriptor, location, PropertyName, DiagnosticMessage);
        }

        public string Render(List<ReactiveModel> properties)
        {
            var taken = new HashSet<string>(StringComparer.Ordinal);
            var existing = ExistingMembers.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var name in existing)
                taken.Add(name);

            var sb = new StringBuilder();
            sb.Append("// <auto-generated/>\n");
            sb.Append("#pragma warning disable\n");
            sb.Append(Open);

            for (var i = 0; i < properties.Count; i++)
            {
                var property = properties[i];
                var field = Pick(property.PreferredField, taken);
                if (i != 0)
                    sb.Append('\n');
                AppendProperty(sb, property, field);
            }

            sb.Append(Close);
            return sb.ToString();
        }

        static void AppendProperty(StringBuilder sb, ReactiveModel property, string field)
        {
            var indent = property.MemberIndent;
            sb.Append(indent);
            sb.Append("global::Fluorine.Register<");
            sb.Append(property.PropertyType);
            sb.Append("> ");
            sb.Append(field);
            sb.Append(" = new global::Fluorine.Register<");
            sb.Append(property.PropertyType);
            sb.Append(">();\n\n");

            if (property.EmitAutoNotify)
            {
                sb.Append(indent);
                sb.Append("[global::Fluorine.AutoNotify]\n");
            }

            sb.Append(indent);
            if (property.Modifiers.Length != 0)
            {
                sb.Append(property.Modifiers);
                sb.Append(' ');
            }
            sb.Append("partial ");
            sb.Append(property.PropertyType);
            sb.Append(' ');
            sb.Append(property.PropertyName);
            sb.Append("\n");
            sb.Append(indent);
            sb.Append("{\n");
            sb.Append(indent);
            sb.Append("    ");
            sb.Append(property.GetterPrefix);
            sb.Append("get => ");
            sb.Append(field);
            sb.Append(".Value;\n");
            sb.Append(indent);
            sb.Append("    ");
            sb.Append(property.SetterPrefix);
            sb.Append(property.SetterKeyword);
            sb.Append(" => ");
            sb.Append(field);
            sb.Append(".Value = value;\n");
            sb.Append(indent);
            sb.Append("}\n");
        }

        static string Pick(string preferred, HashSet<string> taken)
        {
            if (taken.Add(preferred))
                return preferred;
            var underscored = "_" + preferred.TrimStart('@');
            if (taken.Add(underscored))
                return underscored;
            var n = 2;
            var candidate = preferred + n;
            while (!taken.Add(candidate))
            {
                n++;
                candidate = preferred + n;
            }
            return candidate;
        }

        static ReactiveModel Fail(IPropertySymbol property, PropertyDeclarationSyntax syntax, string id, string message)
        {
            var type = property.ContainingType;
            var key = type == null
                ? property.Name
                : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var span = syntax.Span;
            var line = syntax.SyntaxTree.GetLineSpan(span).Span;
            return new ReactiveModel
            {
                TypeKey = key,
                PropertyName = property.Name,
                DiagnosticId = id,
                DiagnosticMessage = message,
                Path = syntax.SyntaxTree.FilePath ?? "",
                SpanStart = span.Start,
                SpanLength = span.Length,
                StartLine = line.Start.Line,
                StartColumn = line.Start.Character,
                EndLine = line.End.Line,
                EndColumn = line.End.Character,
                ExistingMembers = "",
                Modifiers = "",
                GetterPrefix = "",
                SetterPrefix = "",
                SetterKeyword = "set",
                PreferredField = "",
                PropertyType = "",
                Open = "",
                Close = "",
                MemberIndent = "",
                HintName = ""
            };
        }

        static bool IsOpenGetSet(
            PropertyDeclarationSyntax syntax,
            out AccessorDeclarationSyntax getter,
            out AccessorDeclarationSyntax setter)
        {
            getter = null;
            setter = null;
            if (syntax.ExpressionBody != null || syntax.AccessorList == null)
                return false;

            foreach (var accessor in syntax.AccessorList.Accessors)
            {
                if (accessor.Body != null || accessor.ExpressionBody != null)
                    return false;
                var kind = accessor.Keyword.Kind();
                if (kind == SyntaxKind.GetKeyword)
                    getter = accessor;
                else if (kind == SyntaxKind.SetKeyword || kind == SyntaxKind.InitKeyword)
                    setter = accessor;
                else
                    return false;
            }

            return getter != null && setter != null;
        }

        static bool TypeIsPartial(INamedTypeSymbol type)
        {
            foreach (var reference in type.DeclaringSyntaxReferences)
            {
                var declaration = reference.GetSyntax() as TypeDeclarationSyntax;
                if (declaration != null && HasModifier(declaration.Modifiers, SyntaxKind.PartialKeyword))
                    return true;
            }
            return false;
        }

        static bool HasAttribute(ISymbol symbol, string metadataName)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                var cls = attribute.AttributeClass;
                if (cls != null && cls.ToDisplayString() == metadataName)
                    return true;
            }
            return false;
        }

        static bool HasModifier(SyntaxTokenList modifiers, SyntaxKind kind)
        {
            foreach (var modifier in modifiers)
            {
                if (modifier.IsKind(kind))
                    return true;
            }
            return false;
        }

        static string ModifiersExceptPartial(SyntaxTokenList modifiers)
        {
            var sb = new StringBuilder();
            foreach (var modifier in modifiers)
            {
                if (modifier.IsKind(SyntaxKind.PartialKeyword))
                    continue;
                if (sb.Length != 0)
                    sb.Append(' ');
                sb.Append(modifier.Text);
            }
            return sb.ToString();
        }

        static string Prefix(AccessorDeclarationSyntax accessor)
        {
            var text = ModifiersExceptPartial(accessor.Modifiers);
            if (text.Length == 0)
                return "";
            return text + " ";
        }

        static string FieldName(string propertyName)
        {
            var camel = char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
            if (SyntaxFacts.GetKeywordKind(camel) != SyntaxKind.None)
                return "@" + camel;
            return camel;
        }

        static string Members(INamedTypeSymbol type)
        {
            var names = new List<string>();
            foreach (var name in type.MemberNames)
                names.Add(name);
            names.Sort(StringComparer.Ordinal);
            return string.Join("\n", names);
        }

        static string MakeHintName(INamedTypeSymbol type)
        {
            var raw = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (raw.StartsWith("global::", StringComparison.Ordinal))
                raw = raw.Substring("global::".Length);
            var sb = new StringBuilder("Fluorine.Reactive.");
            foreach (var c in raw)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            sb.Append(".g.cs");
            return sb.ToString();
        }

        static void BuildShell(INamedTypeSymbol type, out string open, out string close, out string indent)
        {
            var chain = new List<INamedTypeSymbol>();
            for (var current = type; current != null; current = current.ContainingType)
                chain.Add(current);
            chain.Reverse();

            var sb = new StringBuilder();
            var closes = new List<string>();
            indent = "";
            var ns = type.ContainingNamespace;
            if (ns != null && !ns.IsGlobalNamespace)
            {
                sb.Append("namespace ").Append(ns.ToDisplayString()).Append("\n{\n");
                indent = "    ";
            }

            foreach (var current in chain)
            {
                sb.Append(indent).Append(TypeDeclaration(current)).Append("\n");
                sb.Append(indent).Append("{\n");
                closes.Insert(0, indent + "}\n");
                indent += "    ";
            }

            if (ns != null && !ns.IsGlobalNamespace)
                closes.Add("}\n");

            var end = new StringBuilder();
            foreach (var line in closes)
                end.Append(line);
            open = sb.ToString();
            close = end.ToString();
        }

        static string TypeDeclaration(INamedTypeSymbol type)
        {
            var sb = new StringBuilder("partial ");
            if (type.IsRecord)
                sb.Append(type.IsValueType ? "record struct " : "record ");
            else if (type.IsValueType)
                sb.Append("struct ");
            else
                sb.Append("class ");

            sb.Append(type.Name);
            if (type.TypeParameters.Length == 0)
                return sb.ToString();

            sb.Append('<');
            for (var i = 0; i < type.TypeParameters.Length; i++)
            {
                if (i != 0)
                    sb.Append(", ");
                sb.Append(type.TypeParameters[i].Name);
            }
            sb.Append('>');
            return sb.ToString();
        }

        static readonly SymbolDisplayFormat TypeFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Included,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

        static readonly DiagnosticDescriptor MustBePartial = new DiagnosticDescriptor(
            Ids.Partial,
            "Reactive property must be partial",
            "Property '{0}' is marked [Reactive]. It must {1}, so Fluorine can generate its register and accessors.",
            "Fluorine",
            DiagnosticSeverity.Error,
            true);

        static readonly DiagnosticDescriptor TypeMustBePartial = new DiagnosticDescriptor(
            Ids.Type,
            "Reactive type must be partial",
            "Property '{0}' is marked [Reactive]. Its type must {1}, so Fluorine can generate the register beside it.",
            "Fluorine",
            DiagnosticSeverity.Error,
            true);

        static readonly DiagnosticDescriptor MustBeInstance = new DiagnosticDescriptor(
            Ids.Static,
            "Reactive property must be an instance property",
            "Property '{0}' is marked [Reactive]. It must {1}. AutoNotify does not watch static properties.",
            "Fluorine",
            DiagnosticSeverity.Error,
            true);

        static readonly DiagnosticDescriptor MustHaveAccessors = new DiagnosticDescriptor(
            Ids.Shape,
            "Reactive property must be a get/set auto-property",
            "Property '{0}' is marked [Reactive]. It must {1}.",
            "Fluorine",
            DiagnosticSeverity.Error,
            true);

        static class Ids
        {
            public const string Partial = "FLUORINE001";
            public const string Type = "FLUORINE002";
            public const string Shape = "FLUORINE003";
            public const string Static = "FLUORINE004";
        }
    }
}
