using System.Collections.Concurrent;
using Genesyslab.Platform.Commons.Protocols;
using Genesyslab.Platform.Commons.Collections;
using Genesyslab.Platform.Commons.Connection;
using Genesyslab.Platform.Voice.Protocols;
using Genesyslab.Platform.Voice.Protocols.TServer;
using Genesyslab.Platform.Voice.Protocols.TServer.Requests.Agent;
using Genesyslab.Platform.Voice.Protocols.TServer.Requests.Dn;
using Genesyslab.Platform.Voice.Protocols.TServer.Requests.Party;
using System;
using System.Collections.Generic;
using System.Threading;
using ServerCRM.Models;
using Genesyslab.Platform.Voice.Protocols.TServer.Events;
using Microsoft.AspNetCore.SignalR;
using Genesyslab.Platform.Voice.Protocols.PreviewInteraction;


namespace ServerCRM.Services
{

    public static class CTIConnectionManager
    {
        private static IHubContext<CtiHub> _hubContext;

        public static void Configure(IHubContext<CtiHub> hubContext)
        {
            _hubContext = hubContext;
        }
        private static readonly ConcurrentDictionary<string, TServerProtocol> agentConnections = new();
        private static readonly object syncLock = new();
        private static readonly ConcurrentDictionary<string, AgentSession> agentSessions = new();

        public static bool LoginAgent(string agentId, string dn, string tServerIp, string tServerPort, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                lock (syncLock)
                {
                    if (agentConnections.ContainsKey(agentId))
                    {
                        errorMessage = "Agent is already connected.";
                        return true;
                    }
                    var endpoint = new Genesyslab.Platform.Commons.Protocols.Endpoint(new Uri("tcp://" + tServerIp + ":" + tServerPort));
                    var tServer = new TServerProtocol(endpoint)
                    {
                        ClientName = "WebCTI",
                        Timeout = TimeSpan.FromSeconds(10)
                    };

                    tServer.Open();

                    var register = RequestRegisterAddress.Create(dn, RegisterMode.ModeShare, ControlMode.RegisterDefault, AddressType.DN);
                    IMessage regResponse = tServer.Request(register);

                    var login = RequestAgentLogin.Create(dn, AgentWorkMode.ManualIn);
                    login.AgentID = agentId;
                    login.Password = "";
                    IMessage loginResponse = tServer.Request(login);

                    var ready = RequestAgentReady.Create(dn, AgentWorkMode.AutoIn);
                    IMessage readyResponse = tServer.Request(ready);

                    agentConnections[agentId] = tServer;
                    var agentSession = new AgentSession
                    {
                        AgentId = agentId,
                        DN = dn,
                        TServerProtocol = tServer,
                        IsRunning = true,
                        ConnID = null,
                        ConforenceNumber = null
                    };
                    agentSessions[agentId] = agentSession;
                    Task.Run(() => ReceiveLoop(agentSession));
                    return true;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"❌ CTI login failed: {ex.Message}";
                return false;
            }
        }
        private static async Task Broadcast(string message)
        {
            if (_hubContext != null)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveStatus", message);
            }
            else
            {
                string san = "";
            }
        }
        private static async void ReceiveLoop(AgentSession session)
        {
            ConnectionId connID = null;
            while (session.IsRunning)
            {
                try
                {
                    if (session.TServerProtocol.State != ChannelState.Opened)
                        continue;

                    var message = session.TServerProtocol.Receive();

                    if (message != null)
                    {
                        string log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 🔔 Received: {message.GetType().Name}";
                        string status = "";
                        status = HandleCtiEvent(message, session, ref connID);

                        if (!string.IsNullOrEmpty(status))
                        {
                            await Broadcast(status);
                        }
                        LogToFile(log + "\n" + status);
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ ReceiveLoop Error: {ex.Message}");
                }
            }
        }
        private static string HandleCtiEvent(IMessage msg, AgentSession session, ref ConnectionId connID)
        {
            string status = "";

            switch (msg.Name)
            {
                case EventDialing.MessageName:
                    var eventDialing = msg as EventDialing;
                    if (eventDialing != null && eventDialing.ThisDN == session.DN)
                    {
                        connID = eventDialing.ConnID;
                        session.ConnID = connID;
                        status = $"📞 Dialing {eventDialing.OtherDN}";
                    }
                    break;

                case EventNetworkReached.MessageName:
                    var eventNetworkReached = msg as EventNetworkReached;
                    if (eventNetworkReached != null && eventNetworkReached.ThisDN == session.DN)
                    {
                        if (connID == null)
                            connID = eventNetworkReached.ConnID;
                        session.ConnID = connID;
                        status = $"📡 Ringing {eventNetworkReached.OtherDN}";
                    }
                    break;

                case EventRinging.MessageName:
                    var ringing = msg as EventRinging;
                    if (ringing != null)
                        status = $"🔔 Ringing from {ringing.OtherDN}";
                    break;

                case EventEstablished.MessageName:
                    var established = msg as EventEstablished;
                    if (established != null)
                        status = $"✅ Talking with {established.OtherDN}";
                    session.ConnID = connID;
                    break;

                case EventReleased.MessageName:
                    var released = msg as EventReleased;
                    status = "🔚 Call ended";
                    session.ConnID = connID;
                    break;

                default:

                    break;
            }

            return status;
        }
        public static void checkReturnedMessageIVR(IMessage msg, AgentSession session)
        {
            switch (msg.Name)
            {
                case EventDialing.MessageName:
                    var eventDialing = msg as EventDialing;
                    if (eventDialing != null && eventDialing.ThisDN == session.DN)
                    {
                        session.IVRConnID = eventDialing.ConnID;
                    }
                    break;
            }
        }

        public static AgentSession GetAgentSession(string agentId)
        {
            if (agentSessions.TryGetValue(agentId, out var session))
            {
                return session;
            }
            throw new Exception("❌ No session found for the agent.");
        }

        public static void MakeCall(string exten, string agentId, string phoneNumber)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];

            var request = RequestMakeCall.Create(exten, phoneNumber, MakeCallType.Regular);


            tServer.Request(request);
        }
        public static void Hold(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            RequestHoldCall requestHoldCall = RequestHoldCall.Create(session.DN, session.ConnID);
            var IMassage = tServer.Request(requestHoldCall);
        }
        public static void Unhold(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            RequestRetrieveCall requestRetrieveCall = RequestRetrieveCall.Create(session.DN, session.ConnID);
            var IMassage = tServer.Request(requestRetrieveCall);
        }

        public static void Conference(string agentId, string Number)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            session.ConforenceNumber = Number;
            RequestInitiateConference requestic = RequestInitiateConference.Create(session.DN, session.ConnID, Number);
            var mass = tServer.Request(requestic);

            switch (mass.Name)
            {
                case EventDialing.MessageName:
                    var eventDialing = mass as EventDialing;
                    if (eventDialing != null && eventDialing.ThisDN == session.DN)
                    {
                        session.IVRConnID = eventDialing.ConnID;
                    }
                    break;
            }
        }
        public static void MergeConference(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            AgentSession session = GetAgentSession(agentId);

            if (session.ConnID == null || session.IVRConnID == null)
                throw new Exception("❌ One or both ConnIDs are missing. Cannot merge calls.");

            var tServer = session.TServerProtocol;
            var requestCompleteConference = RequestCompleteConference.Create(
               session.DN,
               session.ConnID,
               session.IVRConnID
           );

            tServer.Request(requestCompleteConference);
        }


        public static void AgentBreak(string agentId, string brkstatus)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            AgentSession session = GetAgentSession(agentId);

           
            var tServer = session.TServerProtocol;
            KeyValueCollection reasonCodes = new KeyValueCollection();
            reasonCodes.Add("ReasonCode", brkstatus);
            RequestAgentNotReady requestAgentNotReady = RequestAgentNotReady.Create(session.DN, AgentWorkMode.AuxWork, null, reasonCodes, reasonCodes);
            var iMassage = tServer.Request(requestAgentNotReady);
        }


        public static void PartyDelete(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);

            if (session.ConnID == null)
                throw new Exception("⚠️ No active agent call found.");

            if (session.IVRConnID == null)
                throw new Exception("⚠️ No IVR leg found to release.");
            if (session.IVRConnID != null)
            {
                RequestDeleteFromConference releaseIVR = RequestDeleteFromConference.Create(session.DN, session.ConnID, session.ConforenceNumber);
                var massage = tServer.Request(releaseIVR);
                session.IVRConnID = null;
                session.ConforenceNumber = null;
            }
        }



        public static void Disconnect(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];

            AgentSession session = GetAgentSession(agentId);

            if (session.ConnID == null)
                throw new Exception("⚠ No active call found to disconnect.");

            var releaseCall = RequestReleaseCall.Create(session.DN, session.ConnID);
            var Imassage = tServer.Request(releaseCall);
            session.ConnID = null;
            session.IVRConnID = null;
        }

        public static void AgentReady(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];

            AgentSession session = GetAgentSession(agentId);

            
            RequestAgentReady requestAgentReady = RequestAgentReady.Create(session.DN, AgentWorkMode.AutoIn);
            
            var Imassage = tServer.Request(requestAgentReady);

        }


        public static void LogoutAgent(string agentId)
        {
            if (agentConnections.TryRemove(agentId, out var tServer))
            {
                try
                {
                    tServer.Close();
                }
                catch
                {
                }
            }
        }


        private static void LogToFile(string text)
        {
            string logFilePath = @"D:\Logs\ServerCRM_Log.txt";

            try
            {
                string directoryPath = Path.GetDirectoryName(logFilePath);
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                using (StreamWriter writer = new StreamWriter(logFilePath, append: true))
                {
                    writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}");
                    writer.WriteLine("--------------------------------------------------");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to write to log: {ex.Message}");
            }
        }

    }
}
