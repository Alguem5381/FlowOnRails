using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FlowOnRails.AspNetCore.Translation;

/// <summary>Describes HTTP success while translating failures in the current request.</summary>
public static class RailResultExtensions
{
    extension(Rail rail)
    {
        /// <summary>Creates the supplied HTTP result only on success.</summary>
        public IResult ToHttpResult(Func<IResult> onSuccess)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);

            if (!rail.TryUnwrap(out var error))
                return new ErrorHttpResult(error);

            return onSuccess() ?? throw new InvalidOperationException("The success factory returned null.");
        }

        /// <summary>Creates a native MVC result on success, preserving MVC execution and formatters.</summary>
        public ActionResult ToActionResult(Func<IActionResult> onSuccess)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);

            if (!rail.TryUnwrap(out var error))
                return new ErrorActionResult(error);

            var result = onSuccess() ?? throw new InvalidOperationException("The success factory returned null.");

            return result as ActionResult ?? new NativeActionResult(result);
        }
    }

    extension<TValue>(RailFor<TValue> rail) where TValue : notnull
    {
        /// <summary>Creates the supplied HTTP result from the successful value.</summary>
        public IResult ToHttpResult(Func<TValue, IResult> onSuccess)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);

            if (!rail.TryUnwrap(out var value, out var error))
                return new ErrorHttpResult(error);

            return onSuccess(value) ?? throw new InvalidOperationException("The success factory returned null.");
        }

        /// <summary>Creates a native MVC result from the successful value.</summary>
        public ActionResult ToActionResult(Func<TValue, IActionResult> onSuccess)
        {
            ArgumentNullException.ThrowIfNull(onSuccess);

            if (!rail.TryUnwrap(out var value, out var error))
                return new ErrorActionResult(error);

            var result = onSuccess(value) ?? throw new InvalidOperationException("The success factory returned null.");

            return result as ActionResult ?? new NativeActionResult(result);
        }
    }
}
