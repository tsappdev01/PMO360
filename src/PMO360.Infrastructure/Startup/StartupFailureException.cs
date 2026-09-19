namespace PMO360.Infrastructure.Startup;

/// <summary>
/// A failure that stops the portal starting, carrying a message written to be read rather than
/// debugged. <c>Program.cs</c> catches it and prints the message on its own — no stack trace,
/// because there is nothing in the stack that helps the person who has to fix it.
/// </summary>
public sealed class StartupFailureException(string message, Exception? innerException = null)
    : Exception(message, innerException);
