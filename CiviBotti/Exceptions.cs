using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CiviBotti {

    [Serializable()]
    public class DatabaseOpenFail : System.Exception {
        public DatabaseOpenFail() : base() { }
        public DatabaseOpenFail(string message) : base(message) { }
        public DatabaseOpenFail(string message, System.Exception inner) : base(message, inner) { }

        // A constructor is needed for serialization when an
        // exception propagates from a remoting server to the client. 
        protected DatabaseOpenFail(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) { }
    }

    [Serializable()]
    public class DatabaseQueryFail : System.Exception {
        public DatabaseQueryFail() : base() { }
        public DatabaseQueryFail(string message) : base(message) { }
        public DatabaseQueryFail(string message, System.Exception inner) : base(message, inner) { }

        // A constructor is needed for serialization when an
        // exception propagates from a remoting server to the client. 
        protected DatabaseQueryFail(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) { }
    }

    [Serializable()]
    public class DatabaseUnknownType: System.Exception {
        public DatabaseUnknownType() : base() { }
        public DatabaseUnknownType(string message) : base(message) { }
        public DatabaseUnknownType(string message, System.Exception inner) : base(message, inner) { }

        // A constructor is needed for serialization when an
        // exception propagates from a remoting server to the client. 
        protected DatabaseUnknownType(System.Runtime.Serialization.SerializationInfo info,
            System.Runtime.Serialization.StreamingContext context) { }
    }
}
