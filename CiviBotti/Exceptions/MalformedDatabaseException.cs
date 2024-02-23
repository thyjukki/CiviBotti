namespace CiviBotti.Exceptions;

using System;

public class MalformedDatabaseException : Exception
{
    public MalformedDatabaseException(string message) : base(message)
    {
    }
}