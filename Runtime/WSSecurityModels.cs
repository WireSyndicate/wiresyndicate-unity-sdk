using System;

namespace WireSyndicate.SDK
{
    [Serializable]
    internal class HandshakeRequest
    {
        public string network_key;
    }

    [Serializable]
    internal class HandshakeResponse
    {
        public bool success;
        public string session_token;
        public string handshake_secret;
        public int expires_in;
        public string error;
    }
}
