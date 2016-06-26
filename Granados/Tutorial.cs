﻿/*s
 Copyright (c) 2005 Poderosa Project, All Rights Reserved.
 This file is a part of the Granados SSH Client Library that is subject to
 the license included in the distributed package.
 You may not use this file except in compliance with the license.

 $Id: Tutorial.cs,v 1.2 2011/10/27 23:21:56 kzmi Exp $
*/
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Globalization;

using Granados.Crypto;
using Granados.IO;
using Granados.SSH1;
using Granados.SSH2;
using Granados.Util;
using Granados.PKI;
using Granados.KeyboardInteractive;
using Granados.SSH;

namespace Granados.Tutorial {
#if ENABLE_TUTORIAL

    /**
     * Granados Tutorial
     *   To learn the usage of Granados, please read the code in this file.
     */
    /// <exclude/>
    class Tutorial {
        private static SSHConnection _conn;

        [STAThread]
        static void Main(string[] args) {

            //NOTE: modify this number to run these samples!
            int tutorial = 5;

            if (tutorial == 0)
                GenerateRSAKey();
            else if (tutorial == 1)
                GenerateDSAKey();
            else if (tutorial == 2)
                ConnectAndOpenShell();
            else if (tutorial == 3)
                ConnectSSH2AndPortforwarding();
            else if (tutorial == 5)
                AgentForward();
        }

        //Tutorial: Generating a new RSA key for user authentication
        private static void GenerateRSAKey() {
            //RSA KEY GENERATION TEST
            byte[] testdata = Encoding.ASCII.GetBytes("CHRISTIAN VIERI");
            RSAKeyPair kp = RSAKeyPair.GenerateNew(2048, RngManager.GetSecureRng());

            //sign and verify test
            byte[] sig = kp.Sign(testdata);
            kp.Verify(sig, testdata);

            //export / import test
            SSH2UserAuthKey key = new SSH2UserAuthKey(kp);
            key.WritePublicPartInOpenSSHStyle(new FileStream("newrsakey.pub", FileMode.Create));
            key.WritePrivatePartInSECSHStyleFile(new FileStream("newrsakey.bin", FileMode.Create), "comment", "passphrase");
            //read test
            SSH2UserAuthKey newpk = SSH2UserAuthKey.FromSECSHStyleFile("newrsakey.bin", "passphrase");
        }

        //Tutorial: Generating a new DSA key for user authentication
        private static void GenerateDSAKey() {
            //DSA KEY GENERATION TEST
            byte[] testdata = Encoding.ASCII.GetBytes("CHRISTIAN VIERI");
            DSAKeyPair kp = DSAKeyPair.GenerateNew(2048, RngManager.GetSecureRng());

            //sign and verify test
            byte[] sig = kp.Sign(testdata);
            kp.Verify(sig, testdata);

            //export / import test
            SSH2UserAuthKey key = new SSH2UserAuthKey(kp);
            key.WritePublicPartInOpenSSHStyle(new FileStream("newdsakey.pub", FileMode.Create));
            key.WritePrivatePartInSECSHStyleFile(new FileStream("newrsakey.bin", FileMode.Create), "comment", "passphrase");
            //read test
            SSH2UserAuthKey newpk = SSH2UserAuthKey.FromSECSHStyleFile("newrsakey.bin", "passphrase");
        }

        private class SampleKeyboardInteractiveAuthenticationHandler : IKeyboardInteractiveAuthenticationHandler {

            private readonly string _password;

            private readonly AtomicBox<bool> _box = new AtomicBox<bool>();

            public SampleKeyboardInteractiveAuthenticationHandler(string password) {
                _password = password;
            }

            public bool GetResult() {
                bool result = false;
                _box.TryGet(ref result, 10000);
                return result;
            }

            public string[] KeyboardInteractiveAuthenticationPrompt(string[] prompts, bool[] echoes) {
                return prompts.Select(s => s.Contains("assword") ? _password : "").ToArray();
            }

            public void OnKeyboardInteractiveAuthenticationStarted() {
            }

            public void OnKeyboardInteractiveAuthenticationCompleted(bool success, Exception error) {
                _box.TrySet(success, 10000);
            }
        }

        //Tutorial: Connecting to a host and opening a shell
        private static void ConnectAndOpenShell() {
            SampleKeyboardInteractiveAuthenticationHandler authHandler = new SampleKeyboardInteractiveAuthenticationHandler("aaa");
            SSHConnectionParameter f = new SSHConnectionParameter("172.22.1.15", 22, SSHProtocol.SSH2, AuthenticationType.PublicKey, "okajima", "aaa");
            f.EventTracer = new Tracer(); //to receive detailed events, set ISSHEventTracer
            //former algorithm is given priority in the algorithm negotiation
            f.PreferableHostKeyAlgorithms = new PublicKeyAlgorithm[] { PublicKeyAlgorithm.RSA, PublicKeyAlgorithm.DSA };
            f.PreferableCipherAlgorithms = new CipherAlgorithm[] { CipherAlgorithm.Blowfish, CipherAlgorithm.TripleDES };
            f.WindowSize = 0x1000; //this option is ignored with SSH1
            f.KeyboardInteractiveAuthenticationHandler = authHandler;
            Reader reader = new Reader(); //simple event receiver

            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s.Connect(new IPEndPoint(IPAddress.Parse(f.HostName), f.PortNumber)); //22 is the default SSH port

            if (f.AuthenticationType == AuthenticationType.KeyboardInteractive) {
                //Creating a new SSH connection over the underlying socket
                AuthenticationResult authResult;
                _conn = SSHConnection.Connect(f, reader, s, out authResult);
                reader._conn = _conn;
                bool result = authHandler.GetResult();
                Debug.Assert(result == true);
            }
            else {
                //NOTE: if you use public-key authentication, follow this sample instead of the line above:
                //f.AuthenticationType = AuthenticationType.PublicKey;
                f.IdentityFile = "C:\\P4\\tools\\keys\\aaa";
                f.VerifySSHHostKey = (info) => {
                    byte[] h = info.HostKeyFingerPrint;
                    foreach (byte b in h)
                        Debug.Write(String.Format("{0:x2} ", b));
                    return true;
                };

                //Creating a new SSH connection over the underlying socket
                AuthenticationResult authResult;
                _conn = SSHConnection.Connect(f, reader, s, out authResult);
                reader._conn = _conn;
            }

            //Opening a shell
            var ch = _conn.OpenShell(channelOperator => new ChannelHandler(channelOperator));

            // TODO:
            //reader._pf = ch;

            //you can get the detailed connection information in this way:
            //SSHConnectionInfo ci = _conn.ConnectionInfo;

            //Go to sample shell
            SampleShell(reader);
        }

        //Tutorial: port forwarding
        private static void ConnectSSH2AndPortforwarding() {
            SSHConnectionParameter f = new SSHConnectionParameter("10.10.9.8", 22, SSHProtocol.SSH2, AuthenticationType.Password, "root", "");
            f.EventTracer = new Tracer(); //to receive detailed events, set ISSHEventTracer
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s.Connect(new IPEndPoint(IPAddress.Parse(f.HostName), f.PortNumber)); //22 is the default SSH port

            //NOTE: if you use public-key authentication, follow this sample instead of the line above:
            //  f.AuthenticationType = AuthenticationType.PublicKey;
            //  f.IdentityFile = "privatekey.bin";
            //  f.Password = "passphrase";

            //former algorithm is given priority in the algorithm negotiation
            f.PreferableHostKeyAlgorithms = new PublicKeyAlgorithm[] { PublicKeyAlgorithm.DSA };
            f.PreferableCipherAlgorithms = new CipherAlgorithm[] { CipherAlgorithm.Blowfish, CipherAlgorithm.TripleDES };

            f.WindowSize = 0x1000; //this option is ignored with SSH1

            Reader reader = new Reader(); //simple event receiver

            //Creating a new SSH connection over the underlying socket
            AuthenticationResult authResult;
            _conn = SSHConnection.Connect(f, reader, s, out authResult);
            reader._conn = _conn;

            //Local->Remote port forwarding
            ChannelHandler ch = _conn.ForwardPort(
                    channelOperator => new ChannelHandler(channelOperator),
                    "www.google.co.jp", 80u, "localhost", 0u);
            while (!reader._ready)
                System.Threading.Thread.Sleep(100); //wait response
            byte[] data = Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\n\r\n");
            ch.Operator.Send(new DataFragment(data, 0, data.Length)); //get the toppage

            //Remote->Local
            // if you want to listen to a port on the SSH server, follow this line:
            //_conn.ListenForwardedPort("0.0.0.0", 10000);

            //NOTE: if you use SSH2, dynamic key exchange feature is supported.
            //((SSH2Connection)_conn).ReexchangeKeys();
        }

        private static void SampleShell(Reader reader) {
            byte[] b = new byte[1];
            while (true) {
                int input = System.Console.Read();

                b[0] = (byte)input;
                reader._pf.Transmit(b);
            }
        }

        private static void AgentForward() {
            SSHConnectionParameter f = new SSHConnectionParameter("172.22.1.15", 22, SSHProtocol.SSH2, AuthenticationType.Password, "root", "");
            f.EventTracer = new Tracer(); //to receive detailed events, set ISSHEventTracer
            Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            s.Connect(new IPEndPoint(IPAddress.Parse(f.HostName), f.PortNumber)); //22 is the default SSH port

            //former algorithm is given priority in the algorithm negotiation
            f.PreferableHostKeyAlgorithms = new PublicKeyAlgorithm[] { PublicKeyAlgorithm.RSA, PublicKeyAlgorithm.DSA };
            f.PreferableCipherAlgorithms = new CipherAlgorithm[] { CipherAlgorithm.Blowfish, CipherAlgorithm.TripleDES };
            f.WindowSize = 0x1000; //this option is ignored with SSH1
            f.AgentForward = new AgentForwardClient();
            Reader reader = new Reader(); //simple event receiver

            //Creating a new SSH connection over the underlying socket
            AuthenticationResult authResult;
            _conn = SSHConnection.Connect(f, reader, s, out authResult);
            reader._conn = _conn;

            //Opening a shell
            var ch = _conn.OpenShell(channelOperator => new ChannelHandler(channelOperator));

            while (!reader._ready)
                Thread.Sleep(100);

            Thread.Sleep(1000);
            byte[] data = Encoding.Default.GetBytes("ssh -A -l okajima localhost\r");
            ch.Operator.Send(new DataFragment(data, 0, data.Length));

            //Go to sample shell
            SampleShell(reader);
        }

    }

    /// <summary>
    /// 
    /// </summary>
    /// <exclude/>
    class Reader : ISSHConnectionEventReceiver, ISSHChannelEventReceiver {
        public SSHConnection _conn;
        public bool _ready;

        public void OnData(DataFragment data) {
            System.Console.Write(Encoding.ASCII.GetString(data.Data, data.Offset, data.Length));
        }
        public void OnDebugMessage(bool alwaysDisplay, string message) {
            Debug.WriteLine("DEBUG: " + message);
        }
        public void OnIgnoreMessage(byte[] data) {
            Debug.WriteLine("Ignore: " + Encoding.ASCII.GetString(data));
        }

        public void OnError(Exception error) {
            Debug.WriteLine("ERROR: " + error.Message);
            Debug.WriteLine(error.StackTrace);
        }
        public void OnChannelClosed() {
            Debug.WriteLine("Channel closed");
            //_conn.AsyncReceive(this);
        }
        public void OnChannelEOF() {
            _pf.Close();
            Debug.WriteLine("Channel EOF");
        }
        public void OnExtendedData(uint type, DataFragment data) {
            Debug.WriteLine("EXTENDED DATA");
        }
        public void OnConnectionClosed() {
            Debug.WriteLine("Connection closed");
        }
        public void OnUnknownMessage(byte type, byte[] data) {
            Debug.WriteLine("Unknown Message " + type);
        }
        public void OnChannelReady() {
            _ready = true;
        }
        public void OnChannelError(Exception error) {
            Debug.WriteLine("Channel ERROR: " + error.Message);
        }
        public void OnMiscPacket(byte type, DataFragment data) {
        }

        public SSHChannel _pf;
    }

    class ChannelHandler : ISSHChannelEventHandler {

        private readonly ISSHChannel _operator;

        public ISSHChannel Operator {
            get {
                return _operator;
            }
        }

        public ChannelHandler(ISSHChannel channelOperator) {
            _operator = channelOperator;
        }

        public void OnEstablished(DataFragment data) {
            Debug.WriteLine("Channel Established");
        }

        public void OnReady() {
            Debug.WriteLine("Channel Ready");
        }

        public void OnData(DataFragment data) {
            System.Console.Write(Encoding.UTF8.GetString(data.Data, data.Offset, data.Length));
        }

        public void OnExtendedData(uint type, DataFragment data) {
            System.Console.WriteLine("EXT[{0}] {1}", type, Encoding.UTF8.GetString(data.Data, data.Offset, data.Length));
        }

        public void OnClosing(bool byServer) {
            Debug.WriteLine("Channel Closing");
        }

        public void OnClosed(bool byServer) {
            Debug.WriteLine("Channel Closed");
        }

        public void OnEOF() {
            Debug.WriteLine("Channel EOF");
        }

        public void OnRequestFailed() {
            throw new NotImplementedException();
        }

        public void OnError(Exception error) {
            Debug.WriteLine("Channel ERROR: " + error.Message);
            Debug.WriteLine(error.StackTrace);
        }

        public void OnUnhandledPacket(byte packetType, DataFragment data) {
            Debug.WriteLine("Channel Unhandled Packet: {0}", packetType);
        }

        public void Dispose() {
            Debug.WriteLine("Channel Dispose");
        }

    }

    /// <summary>
    /// 
    /// </summary>
    /// <exclude/>
    class Tracer : ISSHEventTracer {
        public void OnTranmission(string type, string detail) {
            Debug.WriteLine("T:" + type + ":" + detail);
        }
        public void OnReception(string type, string detail) {
            Debug.WriteLine("R:" + type + ":" + detail);
        }
    }

    class AgentForwardClient : IAgentForward {
        private SSH2UserAuthKey[] _keys;
        public SSH2UserAuthKey[] GetAvailableSSH2UserAuthKeys() {
            if (_keys == null) {
                SSH2UserAuthKey k = SSH2UserAuthKey.FromSECSHStyleFile(@"C:\P4\Tools\keys\aaa", "aaa");
                _keys = new SSH2UserAuthKey[] { k };
            }
            return _keys;
        }

        public void NotifyPublicKeyDidNotMatch() {
            Debug.WriteLine("KEY NOT MATCH");
        }
        public bool CanAcceptForwarding() {
            return true;
        }

        public void Close() {
        }

        public void OnError(Exception ex) {
        }
    }
#endif
}
