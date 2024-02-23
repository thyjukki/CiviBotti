namespace CiviBotti.Exceptions;

using System;

[Serializable]
public class MalformedDatabaseException : Exception
{
    public MalformedDatabaseException(string message)
        : base(message)
    {
    }

    protected MalformedDatabaseException(string message, Exception inner)
        : base(message, inner)
    {
    }
    
    public MalformedDatabaseException()
    {
    }
    
    protected MalformedDatabaseException(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context) : base(info, context)
    {
    }
}