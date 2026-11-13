using System;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis.Internal;

// Encode types structurally, without typeof references to private producer types.
// Type parameters retain their owner and position so constructed consumers can
// substitute them. Unresolvable or inaccessible types widen the error set on read.
internal static class SummaryTypeCodec
{
    internal static void Write(BinaryWriter writer, ITypeSymbol type, IMethodSymbol method, int depth = 0)
    {
        if (depth >= 32)
        {
            writer.Write((byte)0);

            return;
        }

        if (type.SpecialType != SpecialType.None)
        {
            writer.Write((byte)1);
            writer.Write((int)type.SpecialType);

            return;
        }

        if (type is ITypeParameterSymbol parameter)
        {
            if (parameter.DeclaringMethod is { } declaringMethod
                && SymbolEqualityComparer.Default.Equals(declaringMethod.OriginalDefinition, method.OriginalDefinition))
            {
                writer.Write((byte)2);
                writer.Write(parameter.Ordinal);
            }
            else if (parameter.DeclaringType is { } declaringType)
            {
                writer.Write((byte)3);
                writer.Write(MetadataName(declaringType));
                writer.Write(parameter.Ordinal);
            }
            else
                writer.Write((byte)0);

            return;
        }

        if (type is IArrayTypeSymbol array)
        {
            writer.Write((byte)4);
            writer.Write(array.Rank);
            Write(writer, array.ElementType, method, depth + 1);

            return;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            writer.Write((byte)0);

            return;
        }

        writer.Write((byte)5);
        writer.Write(namedType.ContainingAssembly.Identity.ToString());
        writer.Write(MetadataName(namedType));
        writer.Write(namedType.ContainingType is not null);

        if (namedType.ContainingType is { } containingType)
            Write(writer, containingType, method, depth + 1);

        writer.Write(namedType.TypeArguments.Length);

        foreach (var argument in namedType.TypeArguments)
            Write(writer, argument, method, depth + 1);
    }

    internal static ITypeSymbol? Read(
        BinaryReader reader,
        Compilation compilation,
        IMethodSymbol method,
        int depth = 0)
    {
        if (depth >= 32)
            throw new InvalidDataException("Type nesting exceeds the metadata limit.");

        switch (reader.ReadByte())
        {
            case 0:
                return null;

            case 1:
                var specialType = reader.ReadInt32();

                if (specialType <= 0 || specialType > (int)SpecialType.Count)
                    throw new InvalidDataException("Invalid special type.");

                return compilation.GetSpecialType((SpecialType)specialType);

            case 2:
                var methodOrdinal = ReadCount(reader);

                return methodOrdinal < method.TypeArguments.Length ? method.TypeArguments[methodOrdinal] : null;

            case 3:
                var ownerName = reader.ReadString();
                var typeOrdinal = ReadCount(reader);

                for (var owner = method.ContainingType; owner is not null; owner = owner.ContainingType)
                    if (MetadataName(owner) == ownerName)
                        return typeOrdinal < owner.TypeArguments.Length ? owner.TypeArguments[typeOrdinal] : null;

                return null;

            case 4:
                var rank = ReadCount(reader);
                var elementType = Read(reader, compilation, method, depth + 1);

                return elementType is null || rank is < 1 or > 32
                    ? null
                    : compilation.CreateArrayTypeSymbol(elementType, rank);

            case 5:
                var assemblyIdentity = reader.ReadString();
                var metadataName = reader.ReadString();
                var containingType = reader.ReadBoolean()
                    ? Read(reader, compilation, method, depth + 1) as INamedTypeSymbol
                    : null;
                var argumentCount = ReadCount(reader);
                var arguments = new ITypeSymbol?[argumentCount];

                for (var argumentIndex = 0; argumentIndex < argumentCount; argumentIndex++)
                    arguments[argumentIndex] = Read(reader, compilation, method, depth + 1);

                var assembly = compilation.Assembly.Identity.ToString() == assemblyIdentity
                    ? compilation.Assembly
                    : compilation.SourceModule.ReferencedAssemblySymbols.FirstOrDefault(candidate =>
                        candidate.Identity.ToString() == assemblyIdentity);
                var definition = assembly?.GetTypeByMetadataName(metadataName);

                if (definition is null
                    || arguments.Any(argument => argument is null)
                    || definition.Arity != argumentCount
                    || !compilation.IsSymbolAccessibleWithin(definition, compilation.Assembly))
                    return null;

                if (definition.ContainingType is not null)
                {
                    if (containingType is null)
                        return null;

                    definition = containingType.GetTypeMembers(definition.Name, definition.Arity).FirstOrDefault();

                    if (definition is null)
                        return null;
                }

                return argumentCount == 0 ? definition : definition.Construct(arguments.Select(argument => argument!).ToArray());

            default:
                throw new InvalidDataException("Unknown type encoding.");
        }
    }

    internal static int ReadCount(BinaryReader reader)
    {
        var count = reader.ReadInt32();

        if (count is < 0 or > 4096)
            throw new InvalidDataException("Invalid metadata count.");

        return count;
    }

    private static string MetadataName(INamedTypeSymbol type) =>
        type.ContainingType is { } containingType
            ? MetadataName(containingType) + "+" + type.MetadataName
            : (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".")
              + type.MetadataName;
}
