using System;

namespace CiviBotti {

    [Serializable()]
    public class DatabaseOpenFail : Exception {
        public DatabaseOpenFail() { }
        public DatabaseOpenFail(string message) : base(message) { }
        public DatabaseOpenFail(string message, Exception inner) : base(message, inner) { }

        // A constructor is needed for serialization when an
        // exception propagates from a remoting server to the client. 
        protected DatabaseOpenFail(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) { }
    }

    [Serializable()]
    public class DatabaseQueryFail : Exception {
        public DatabaseQueryFail() { }
        public DatabaseQueryFail(string message) : base(message) { }
        public DatabaseQueryFail(string message, Exception inner) : base(message, inner) { }

        // A constructor is needed for serialization when an
        // exception propagates from a remoting server to the client. 
        protected DatabaseQueryFail(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) { }
    }

    [Serializable()]
    public class DatabaseUnknownType: Exception {
        public DatabaseUnknownType() { }
        public DatabaseUnknownType(string message) : base(message) { }
        public DatabaseUnknownType(string message, Exception inner) : base(message, inner) { }

        // A constructor is needed for serialization when an
        // exception propagates from a remoting server to the client. 
        protected DatabaseUnknownType(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) { }
    }
}
