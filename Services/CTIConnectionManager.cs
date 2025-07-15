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
using Microsoft.Extensions.Logging;
using Genesyslab.Platform.Outbound.Protocols.OutboundDesktop;
using static System.Net.Mime.MediaTypeNames;
using System.Data;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc.Diagnostics;


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
                        ConforenceNumber = null,
                        isOnCall=false,
                        isConforence=false
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
        private static async Task Broadcast(string message, string agentId)
        {

            if (_hubContext != null)
            {
                await _hubContext.Clients.Group(agentId).SendAsync("ReceiveStatus", message);

            }
            else
            {
                string san = "";
            }
        }
        private static async void ReceiveLoop(AgentSession session)
        {
            string status = "";
            ConnectionId connID = null;
            Dictionary<string, string> attachedData = null;
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
                       
                        status = HandleCtiEvent(message, session, ref connID , out attachedData);

                        if (!string.IsNullOrEmpty(status))
                        {
                            await Broadcast(status, session.AgentId);
                        }
                        if (attachedData != null)
                        {
                            
                            await _hubContext.Clients.Group(session.AgentId).SendAsync("ReceiveAttachedData", attachedData);
                        }

                    }
                }
                catch (Exception ex)
                {
                    LogToFile($"❌ ReceiveLoop Error: {ex.Message}");
                }
            }
        }
        private static string HandleCtiEvent(IMessage msg, AgentSession session, ref ConnectionId connID , out Dictionary<string, string> attachedData)
        {
            attachedData = null;
            string status = "";
            lock (session.LockObj)
            {
                switch (msg.Name)
                {
                    case EventDialing.MessageName:
                        var eventDialing = msg as EventDialing;
                        if (eventDialing != null && eventDialing.ThisDN == session.DN)
                        {
                            connID = eventDialing.ConnID;
                            session.ConnID = connID;
                            status = $"📞 DIALING {eventDialing.OtherDN}";
                        }
                        break;

                    case EventNetworkReached.MessageName:
                        var eventNetworkReached = msg as EventNetworkReached;
                        if (eventNetworkReached != null && eventNetworkReached.ThisDN == session.DN)
                        {
                            if (connID == null)
                                connID = eventNetworkReached.ConnID;
                            session.ConnID = connID;
                            status = $"📡 Ringing (Network Reached) {eventNetworkReached.OtherDN}";
                        }
                        break;

                    case EventRinging.MessageName:
                        var ringing = msg as EventRinging;
                        if (ringing != null)
                            status = $"🔔 Ringing at destination: {ringing.OtherDN}";
                        break;

                    case EventEstablished.MessageName:
                        var established = msg as EventEstablished;

                        if (established.ThisDN.ToString().Equals(session.DN))
                        {
                            if (session.ConnID == null && session.IVRConnID == null)
                            {
                                session.ConnID = established.ConnID;
                            }
                        }
                        if (established.CallType == CallType.Inbound)
                        {
                            status = "TALKING";

                        }
                        else
                        {
                            status = "TALKING";

                        }
                        if (established != null)
                          
                        session.ConnID = connID;
                        break;

                    case EventReleased.MessageName:
                        var released = msg as EventReleased;
                        if (released.ThisDN == session.DN)
                        {
                            status = "WRAPING";
                            session.ConnID = connID;
                            session.isOnCall = false;

                        }
                            
                        break;
                    case EventAgentReady.MessageName:
                        try
                        {
                            EventAgentReady eventagentready = msg as EventAgentReady;
                            if (eventagentready.ThisDN == session.DN)
                            {
                                status = "WAITING";
                            }
                        }
                        catch (Exception ex)
                        {
                           
                        }
                        break;
                    case EventAgentNotReady.MessageName:
                        EventAgentNotReady eventagentnotready = msg as EventAgentNotReady;
                        if (eventagentnotready.ThisDN == session.DN)
                        {
                            status = $"Reason: {eventagentnotready.Reasons}";

                        }
                        break;
                    case EventAgentLogout.MessageName:
                        EventAgentLogout eventagentlogout = msg as EventAgentLogout;
                        if (eventagentlogout.ThisDN == session.DN)
                        {
                            status = "🔓 Agent logged out";
                        }
                        break;
                    case EventDNOutOfService.MessageName:
                        EventDNOutOfService eventdNOutOfservice = msg as EventDNOutOfService;
                        if (eventdNOutOfservice.ThisDN == session.DN)
                        {
                            status = "⚠️ DN is out of service";
                        }
                        break;
                    case EventDNBackInService.MessageName:
                        EventDNBackInService eventdnbackinservice = msg as EventDNBackInService;
                        if (eventdnbackinservice.ThisDN == session.DN)
                        {
                            status = "✅ DN back in service";
                        }
                        break;
                    case EventDestinationBusy.MessageName:
                        EventDestinationBusy eventdestinationbusy = msg as EventDestinationBusy;
                        if (eventdestinationbusy.ThisDN == session.DN)
                        {
                            var Tserver = session.TServerProtocol;
                            if(session.IVRConnID !=null)
                            {
                                RequestReleaseCall releasecall = RequestReleaseCall.Create(session.DN, session.IVRConnID);
                                var iMessage = Tserver.Request(releasecall);
                                RequestRetrieveCall retrievecall = RequestRetrieveCall.Create(session.DN, connID);
                                iMessage = Tserver.Request(retrievecall);
                            }
                            else
                            {
                                status = "🚫 Destination is busy";

                            }

                              
                        }
                        break;
                    case EventPartyChanged.MessageName:
                        EventPartyChanged eventPartyChanged = msg as EventPartyChanged;
                        if (eventPartyChanged.ThisDN == session.DN)
                        {
                            status = "🔁 Party changed in the call";
                        }
                        break;
                    case EventPartyDeleted.MessageName:
                        try
                        {
                            EventPartyDeleted eventPartydeleted = msg as EventPartyDeleted;
                            if (eventPartydeleted.ThisDN.ToString().Equals(session.DN))
                            {
                                status = "➖ Party removed from call";
                            }
                        }
                        catch (Exception ex)
                        {
                           
                        }
                        break;
                    case EventPartyAdded.MessageName:
                        try
                        {
                            EventPartyAdded eventPartyAdded = msg as EventPartyAdded;
                            if (eventPartyAdded.ThisDN.ToString().Equals(session.DN))
                            {
                                try
                                {
                                    status = "➕ New party added to call";
                                }
                                catch (Exception ex1)
                                {
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                        
                        }
                        break;
                  
                
                   
                    case EventOnHook.MessageName:
                        EventOnHook eventOnhook = msg as EventOnHook;
                        if (eventOnhook.ThisDN == session.DN)
                        {
                            var Tserver = session.TServerProtocol;
                            RequestAgentNotReady requestAgentNotReady = RequestAgentNotReady.Create(session.DN, AgentWorkMode.AfterCallWork);
                             var   iMessage = Tserver.Request(requestAgentNotReady);
                            status = "📴 Handset on hook";
                        }
                        break;
                    case EventAbandoned.MessageName:
                        EventAbandoned eventAbandoned = msg as EventAbandoned;
                        if (eventAbandoned.ThisDN == session.DN)
                        {
                            status = "🚷 Caller abandoned the call";
                        }
                        break;
                    case EventAttachedDataChanged.MessageName:
                        EventAttachedDataChanged eventattacheddatachanged = msg as EventAttachedDataChanged;
                        if (eventattacheddatachanged.ThisDN == session.DN)
                        {
                            status = "📝 Attached data updated";

                            attachedData = new Dictionary<string, string>();

                            for (int i = 0; i < eventattacheddatachanged.UserData.Count; i++)
                            {
                                var key = eventattacheddatachanged.UserData.Keys[i]?.ToString();
                                var value = eventattacheddatachanged.UserData[i]?.ToString();

                                if (key != null)
                                {
                                    attachedData[key] = value;
                                }
                            }


                        }
                        break;
                    case EventUserEvent.MessageName:
                        EventUserEvent eventUserEvent = msg as EventUserEvent;
                        if (eventUserEvent.ThisDN == session.DN)
                        {
                            status = $"📨 User event received: {eventUserEvent.UserData}";
                        }
                        break;

                    default:
                        status = $"ℹ️ Unhandled event: {msg.Name}";
                        break;
                }
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

        public static async Task MakeCall(string exten, string agentId, string phoneNumber)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            if(session.isOnCall==false)
            {
                var request = RequestMakeCall.Create(session.DN, phoneNumber, MakeCallType.Regular);
                var iMassage = tServer.Request(request);
                if (iMassage.Name == "EventError")
                {
                    await Broadcast("EventError While Call", session.AgentId);
                }
                else
                {
                    session.isOnCall = true;
                    await Broadcast(iMassage.Name, session.AgentId);
                }
            }
            else
            {
                await Broadcast("Agent Is Already On Call", session.AgentId);
            }
        }
        public static async Task Hold(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            if(session.isOnCall==true)
            {
                RequestHoldCall requestHoldCall = RequestHoldCall.Create(session.DN, session.ConnID);
                var IMassage = tServer.Request(requestHoldCall);
                if (IMassage.Name == "EventError")
                {
                    await Broadcast("EventError While Hold", session.AgentId);
                }
                else
                {
                   
                    await Broadcast(IMassage.Name, session.AgentId);
                }
            }
            else
            {

            }
           
        }
        public static async Task Unhold(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            if (session.isOnCall == true)
            {
                RequestRetrieveCall requestRetrieveCall = RequestRetrieveCall.Create(session.DN, session.ConnID);
                var IMassage = tServer.Request(requestRetrieveCall);
                if (IMassage.Name == "EventError")
                {
                    await Broadcast("EventError While Unhold", session.AgentId);
                }
                else
                {

                    await Broadcast(IMassage.Name, session.AgentId);
                }
            }
        }

        public static void Conference(string agentId, string Number)
        {

            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            lock (session.LockObj)
            {
                session.ConforenceNumber = Number;
                if(session.isOnCall==true)
                {
                    if(session.isConforence==false)
                    {
                        RequestInitiateConference requestic = RequestInitiateConference.Create(session.DN, session.ConnID, Number);
                        var IMassage = tServer.Request(requestic);
                        if (IMassage.Name == "EventError")
                        {
                            session.isConforence = false;

                        }
                        else
                        {
                            session.isConforence = true;
                        }
                        switch (IMassage.Name)
                        {
                            case EventDialing.MessageName:
                                var eventDialing = IMassage as EventDialing;
                                if (eventDialing != null && eventDialing.ThisDN == session.DN)
                                {
                                    session.IVRConnID = eventDialing.ConnID;
                                }
                                break;
                        }
                    }
                }
               
               
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
            if(session.isOnCall==true)
            {
                var requestCompleteConference = RequestCompleteConference.Create(
               session.DN,
               session.ConnID,
               session.IVRConnID);

                tServer.Request(requestCompleteConference);

            }
           
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


        public static void transferCall(string agentId, string routePoint)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("❌ Agent not logged in.");

            AgentSession session = GetAgentSession(agentId);

            var tServer = session.TServerProtocol;
            string RouteValue = Convert.ToString(routePoint);
            RequestInitiateConference requestic = RequestInitiateConference.Create(session.DN, session.ConnID, RouteValue);
            var iMessage = tServer.Request(requestic);
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
                if(session.isConforence==true)
                {
                    RequestDeleteFromConference releaseIVR = RequestDeleteFromConference.Create(session.DN, session.ConnID, session.ConforenceNumber);
                    var IMassage = tServer.Request(releaseIVR);
                    if (IMassage.Name == "EventError")
                    {
                        

                    }
                    else
                    {
                        session.IVRConnID = null;
                        session.ConforenceNumber = null;
                        session.isConforence = false;

                    }
                       
                }
               
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
            if(session.IVRConnID == null && session.isConforence==false)
            {
                var releaseCall = RequestReleaseCall.Create(session.DN, session.ConnID);
                var Imassage = tServer.Request(releaseCall);
                session.ConnID = null;

            }
           
         
            
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
