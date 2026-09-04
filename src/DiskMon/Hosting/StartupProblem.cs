using System.Text;
using Microsoft.Extensions.Options;

namespace DiskMon.Hosting;

/// <summary>
/// Turns a startup failure into something an operator can act on.
/// </summary>
/// <remarks>
/// The two ways DiskMon fails to start are a settings file it cannot parse and a settings file it
/// cannot accept, and both arrive wrapped in framework exceptions whose stack traces say nothing
/// useful about either. What the reader needs is the line number or the setting name.
/// </remarks>
public static class StartupProblem
{
    /// <summary>Whether an exception is a settings problem rather than a bug.</summary>
    /// <param name="exception">The exception thrown during startup.</param>
    /// <returns>True if it should be reported as a settings problem.</returns>
    public static bool IsSettingsProblem(Exception exception)
        => Unwrap(exception) is FormatException or InvalidDataException
            or OptionsValidationException;

    /// <summary>
    /// Describes a startup failure in the terms the settings file uses.
    /// </summary>
    /// <param name="exception">The exception thrown during startup.</param>
    /// <returns>A message ready to print, without a stack trace.</returns>
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var root = Unwrap(exception);

        if (root is OptionsValidationException validation)
        {
            var report = new StringBuilder("settings.xml has values DiskMon cannot use:")
                .AppendLine()
                .AppendLine();

            foreach (var failure in validation.Failures)
                report.Append("  - ").AppendLine(failure);

            return report.ToString().TrimEnd();
        }

        return root.Message;
    }

    /// <summary>
    /// Finds the exception worth reporting. The framework wraps a configuration parse failure in
    /// an <see cref="InvalidDataException"/> that names the file but not the problem.
    /// </summary>
    private static Exception Unwrap(Exception exception)
        => exception is InvalidDataException && exception.InnerException is { } inner
            ? Unwrap(inner)
            : exception;
}
