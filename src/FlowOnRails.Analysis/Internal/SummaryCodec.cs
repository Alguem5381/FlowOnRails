using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

using Microsoft.CodeAnalysis;

namespace FlowOnRails.Analysis.Internal;

internal static class SummaryCodec
{
    internal const int SchemaVersion = 3;

    internal static string Encode(MethodFlowResult result, IMethodSymbol method)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        WriteSummary(writer, result, method);
        writer.Write(result.Variants.Length);

        foreach (var variant in result.Variants)
        {
            foreach (var input in variant.Inputs)
                WriteValue(writer, input, method);

            WriteSummary(writer, variant.Result, method);
        }

        writer.Flush();

        return Convert.ToBase64String(stream.ToArray());
    }

    internal static MethodFlowResult Decode(
        string payload,
        IMethodSymbol method,
        FlowValue[] arguments,
        LibrarySymbols symbols,
        int version = SchemaVersion)
    {
        if (payload.Length > 1024 * 1024)
            throw new InvalidDataException("Summary metadata is too large.");

        using var stream = new MemoryStream(Convert.FromBase64String(payload));
        using var reader = new BinaryReader(stream);
        var result = ReadSummary(reader, method, arguments, symbols, version);

        if (version >= 2)
        {
            var variantCount = SummaryTypeCodec.ReadCount(reader);
            MethodFlowResult? selected = null;
            var matchingVariants = 0;

            if (variantCount > 16)
                throw new InvalidDataException("Too many guarded summary variants.");

            // Parse every variant even when its guard is disjoint. Unselected
            // malformed metadata must not turn a partial payload into a certificate.
            for (var variantIndex = 0; variantIndex < variantCount; variantIndex++)
            {
                var narrowedArguments = new FlowValue[method.Parameters.Length];
                var matches = true;
                var guardLimitations = ImmutableArray.CreateBuilder<ErrorFlowLimitation>();

                foreach (var parameter in method.Parameters)
                {
                    var guard = ReadValue(reader, method, [], symbols, guardLimitations, 0, version);
                    var argument = parameter.Ordinal < arguments.Length && arguments[parameter.Ordinal] is { } supplied
                        ? supplied : symbols.Unknown(parameter.Type);
                    narrowedArguments[parameter.Ordinal] = GuardedSummary.Intersect(argument, guard)
                        .WithDependencyFrom(argument);
                    matches &= !narrowedArguments[parameter.Ordinal].IsBottom;
                }

                var candidate = ReadSummary(reader, method, narrowedArguments, symbols, version);
                candidate = new MethodFlowResult(candidate.ReturnValue, candidate.UnresolvedConditions,
                    [.. candidate.Limitations.Union(guardLimitations)], candidate.ParameterOutputs);

                if (matches)
                {
                    matchingVariants++;
                    selected = selected is null ? candidate : Join(selected, candidate);
                }
            }

            if (variantCount > 0 && selected is not null)
                result = new MethodFlowResult(selected.ReturnValue,
                    selected.UnresolvedConditions || matchingVariants > 1 && result.UnresolvedConditions,
                    selected.Limitations, selected.ParameterOutputs);
        }

        if (stream.Position != stream.Length)
            throw new InvalidDataException("Unexpected summary data.");

        return result;
    }

    private static void WriteSummary(BinaryWriter writer, MethodFlowResult result, IMethodSymbol method)
    {
        WriteValue(writer, result.ReturnValue, method);
        writer.Write(result.UnresolvedConditions);
        writer.Write(result.Limitations.Length);

        foreach (var limitation in result.Limitations)
        {
            writer.Write((int)limitation.Code);
            writer.Write(limitation.Message);
        }

        writer.Write(result.ParameterOutputs.Length);

        foreach (var output in result.ParameterOutputs)
            WriteValue(writer, output, method);
    }

    private static MethodFlowResult ReadSummary(
        BinaryReader reader,
        IMethodSymbol method,
        FlowValue[] arguments,
        LibrarySymbols symbols,
        int version)
    {
        var limitations = ImmutableArray.CreateBuilder<ErrorFlowLimitation>();
        var value = ReadValue(reader, method, arguments, symbols, limitations, 0, version);
        var unresolved = reader.ReadBoolean();
        var limitationCount = SummaryTypeCodec.ReadCount(reader);

        for (var limitationIndex = 0; limitationIndex < limitationCount; limitationIndex++)
        {
            // Older formats contain presentation text only. Never infer a reason
            // by matching that text. Future reason IDs within this schema retain
            // their explanation as Unclassified without changing the error set.
            var code = version >= 3 ? reader.ReadInt32() : 0;
            var classification = Enum.IsDefined(typeof(ErrorFlowLimitationCode), code)
                ? (ErrorFlowLimitationCode)code : ErrorFlowLimitationCode.Unclassified;
            limitations.Add(new ErrorFlowLimitation(classification, reader.ReadString()));
        }

        var outputs = ImmutableArray.CreateBuilder<FlowValue>();

        if (version >= 2)
        {
            var outputCount = SummaryTypeCodec.ReadCount(reader);

            if (outputCount != 0 && outputCount != method.Parameters.Length)
                throw new InvalidDataException("Invalid summary parameter outputs.");

            for (var outputIndex = 0; outputIndex < outputCount; outputIndex++)
                outputs.Add(ReadValue(reader, method, arguments, symbols, limitations, 0, version));
        }

        if (!symbols.MatchesReturnType(method.ReturnType, value))
            throw new InvalidDataException("Summary return shape does not match the method.");

        foreach (var parameter in method.Parameters)
            if (parameter.RefKind is RefKind.Ref or RefKind.Out
                && parameter.Ordinal < outputs.Count
                && !symbols.MatchesReturnType(parameter.Type, outputs[parameter.Ordinal]))
                throw new InvalidDataException("Summary output shape does not match the parameter.");

        return new MethodFlowResult(value, unresolved, limitations.ToImmutable(), outputs.ToImmutable());
    }

    private static MethodFlowResult Join(MethodFlowResult first, MethodFlowResult second) =>
        new MethodFlowResult(first.ReturnValue.Join(second.ReturnValue),
            first.UnresolvedConditions || second.UnresolvedConditions,
            [.. first.Limitations.Union(second.Limitations)],
            [.. first.ParameterOutputs.Select((output, parameterIndex) => output.Join(second.ParameterOutputs[parameterIndex]))]);

    private static void WriteValue(BinaryWriter writer, FlowValue value, IMethodSymbol method)
    {
        if (value.Kind == ValueKind.Callable)
            value = FlowValue.Other;

        writer.Write((byte)value.Kind);
        writer.Write(value.MaySucceed);
        writer.Write(value.MayBeInvalid);
        writer.Write(value.MayBeNull);
        writer.Write(value.Boolean switch { true => 1, false => 0, _ => -1 });
        writer.Write(value.ParameterOrigin ?? -1);
        writer.Write((byte)value.Projection);
        writer.Write(value.Errors.Cases.Length);

        foreach (var errorCase in value.Errors.Cases)
        {
            SummaryTypeCodec.Write(writer, errorCase.Type, method);
            writer.Write(errorCase.IsExact);
            writer.Write(errorCase.Excluded.Length);

            foreach (var excludedType in errorCase.Excluded)
                SummaryTypeCodec.Write(writer, excludedType, method);
        }

        if (value.AwaitedValue is { } awaitedValue)
            WriteValue(writer, awaitedValue, method);
    }

    // Read the complete payload before applying a parameter dependency. Invalid
    // data must not become a closed summary merely because an argument is known.
    // Unavailable private types widen only the affected result, retaining known types.
    private static FlowValue ReadValue(
        BinaryReader reader,
        IMethodSymbol method,
        FlowValue[] arguments,
        LibrarySymbols symbols,
        ImmutableArray<ErrorFlowLimitation>.Builder limitations,
        int depth,
        int version)
    {
        if (depth >= 32)
            throw new InvalidDataException("Summary nesting exceeds the metadata limit.");

        var kind = (ValueKind)reader.ReadByte();
        var maySucceed = reader.ReadBoolean();
        var mayBeInvalid = reader.ReadBoolean();
        var mayBeNull = reader.ReadBoolean();
        var boolean = reader.ReadInt32();
        var parameterOrdinal = reader.ReadInt32();
        var projection = version >= 2 ? (ParameterProjection)reader.ReadByte() : ParameterProjection.Identity;

        if (boolean is < -1 or > 1 || parameterOrdinal < -1 || parameterOrdinal >= method.Parameters.Length
            || projection is < ParameterProjection.Identity or > ParameterProjection.RailErrorsToFailure)
            throw new InvalidDataException("Invalid summary value.");

        var errors = ErrorSet.Empty;
        var errorCount = SummaryTypeCodec.ReadCount(reader);

        for (var errorIndex = 0; errorIndex < errorCount; errorIndex++)
        {
            var type = SummaryTypeCodec.Read(reader, symbols.Compilation, method);
            var exact = reader.ReadBoolean();
            var excludedCount = SummaryTypeCodec.ReadCount(reader);
            var excludedTypes = ImmutableArray.CreateBuilder<ITypeSymbol>();

            for (var excludedIndex = 0; excludedIndex < excludedCount; excludedIndex++)
            {
                var excludedType = SummaryTypeCodec.Read(reader, symbols.Compilation, method);

                if (excludedType is not null && symbols.IsError(excludedType))
                    excludedTypes.Add(excludedType);
            }

            if (type is null || !symbols.IsError(type))
            {
                errors = errors.Union(ErrorSet.Of(symbols.Error, false));
                limitations.Add(new ErrorFlowLimitation(
                    ErrorFlowLimitationCode.MetadataTypeUnavailable, "A summary error type is unavailable or inaccessible in this compilation"));

                continue;
            }

            errors = errors.Union(new ErrorSet([new ErrorSet.ErrorCase(type, exact, excludedTypes.ToImmutable())]));
        }

        var value = kind switch
        {
            ValueKind.Bottom => FlowValue.Bottom,
            ValueKind.Other => FlowValue.Other,
            ValueKind.Rail => FlowValue.Rail(errors, maySucceed, mayBeInvalid),
            ValueKind.Error => FlowValue.Error(errors, mayBeNull),
            ValueKind.Boolean => FlowValue.Bool(boolean < 0 ? null : boolean == 1),
            ValueKind.Awaitable => FlowValue.Awaitable(ReadValue(reader, method, arguments, symbols, limitations, depth + 1, version)),
            _ => throw new InvalidDataException("Unknown summary value kind.")
        };

        if (parameterOrdinal < 0)
            return value;

        var argumentValue = parameterOrdinal < arguments.Length ? arguments[parameterOrdinal] : null;

        if (argumentValue is null)
            return value;

        var projected = projection switch
        {
            ParameterProjection.Identity => argumentValue,
            ParameterProjection.ErrorToFailure when argumentValue.Kind == ValueKind.Error =>
                FlowValue.Rail(argumentValue.Errors, false),
            ParameterProjection.RailToError when argumentValue.Kind == ValueKind.Rail =>
                FlowValue.Error(argumentValue.Errors, argumentValue.MaySucceed),
            ParameterProjection.RailErrorsToFailure when argumentValue.Kind == ValueKind.Rail =>
                FlowValue.Rail(argumentValue.Errors, false),
            _ => throw new InvalidDataException("Invalid summary parameter projection.")
        };
        var composed = projection switch
        {
            ParameterProjection.Identity => argumentValue.Projection,
            ParameterProjection.ErrorToFailure => argumentValue.Projection == ParameterProjection.RailToError
                ? ParameterProjection.RailErrorsToFailure : ParameterProjection.ErrorToFailure,
            ParameterProjection.RailToError => argumentValue.Projection == ParameterProjection.ErrorToFailure
                ? ParameterProjection.Identity : ParameterProjection.RailToError,
            _ => argumentValue.Projection == ParameterProjection.ErrorToFailure
                ? ParameterProjection.ErrorToFailure : ParameterProjection.RailErrorsToFailure
        };

        return GuardedSummary.Intersect(projected, value).WithDependencyFrom(argumentValue, composed);
    }
}
