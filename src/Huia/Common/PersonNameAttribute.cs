using System.ComponentModel.DataAnnotations;
using Huia.Localization;
using Microsoft.Extensions.Localization;

namespace Huia.Common;

/// <summary>
/// DataAnnotations wrapper around <see cref="PersonNameValidator"/> for the Identity UI's
/// <c>InputModel</c>s — checks length and character set, not requiredness (pair with <see cref="RequiredAttribute"/>
/// for that, same as every other required field in these forms); an empty/missing value is left for
/// <see cref="RequiredAttribute"/> to report so the two don't produce duplicate errors on the same field.
/// </summary>
/// <remarks>
/// Set <see cref="ValidationAttribute.ErrorMessage"/> explicitly at every usage site (e.g.
/// <c>[PersonName(ErrorMessage = "First name may only contain...")]</c>), the same convention every other
/// validation attribute on these <c>InputModel</c>s follows: the literal string becomes the
/// <c>HuiaResources.resx</c>/<c>HuiaResources.ar.resx</c> lookup key. Unlike built-in attributes
/// (<c>[Required]</c>, <c>[EmailAddress]</c>, ...), <c>AddDataAnnotationsLocalization</c>'s own
/// <c>DataAnnotationsModelValidatorProvider</c> doesn't re-localize a <em>custom</em>
/// <see cref="ValidationAttribute"/> subclass's server-side <see cref="ValidationResult"/> message the same
/// way it does for those (confirmed empirically: the client-side <c>data-val-required</c> hint localizes
/// correctly, but the rendered server-side error stayed English) — so this resolves
/// <see cref="IStringLocalizer{HuiaResources}"/> itself, via <see cref="ValidationContext.GetService"/>,
/// rather than relying on that provider.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
internal sealed class PersonNameAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string name || string.IsNullOrEmpty(name))
        {
            return ValidationResult.Success;
        }

        if (PersonNameValidator.IsValid(name))
        {
            return ValidationResult.Success;
        }

        var template = FormatErrorMessage(validationContext.DisplayName);
        var localizer =
            (IStringLocalizer<HuiaResources>?)validationContext.GetService(typeof(IStringLocalizer<HuiaResources>));
        var message = localizer is null ? template : localizer[template].Value;

        return new ValidationResult(message, validationContext.MemberName is null ? null : [validationContext.MemberName]);
    }
}
