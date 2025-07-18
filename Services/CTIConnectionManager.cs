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
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Genesyslab.Platform.Voice.Protocols.TServer.Requests.Special;


namespace ServerCRM.Services
{

    public static class CTIConnectionManager
    {
        public static IHubContext<CtiHub> _hubContext;

        public static void Configure(IHubContext<CtiHub> hubContext)
        {
            _hubContext = hubContext;
        }
        public static IHubContext<CtiHub> HubContext => _hubContext;
        private static readonly ConcurrentDictionary<string, TServerProtocol> agentConnections = new();
        private static readonly object syncLock = new();
        private static readonly ConcurrentDictionary<string, AgentSession> agentSessions = new();

        public static bool LoginAgent(CL_AgentDet agentvalue, string agentId, string dn, string tServerIp, string tServerPort, out string errorMessage)
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
                        ClientName = Convert.ToString(agentvalue.login_code),
                        Timeout = TimeSpan.FromSeconds(10)
                    };

                    tServer.Open();
                    if (tServer.State == ChannelState.Opened)
                    {
                        var register = RequestRegisterAddress.Create(dn, RegisterMode.ModeShare, ControlMode.RegisterDefault, AddressType.DN);
                        IMessage regResponse = tServer.Request(register);
                        if (regResponse.Name == "EventRegistered")
                        {
                            var login = RequestAgentLogin.Create(dn, AgentWorkMode.ManualIn);
                            login.AgentID = agentId;
                            login.Password = "";
                            IMessage loginResponse = tServer.Request(login);
                            if (loginResponse.Name == "EventAgentLogin")
                            {
                                var ready = RequestAgentReady.Create(dn, AgentWorkMode.AutoIn);
                                IMessage readyResponse = tServer.Request(ready);
                                if (readyResponse.Name == "EventAgentReady")
                                {
                                    agentConnections[agentId] = tServer;
                                    var agentSession = new AgentSession
                                    {
                                        AgentId = agentId,
                                        DN = dn,
                                        TServerProtocol = tServer,
                                        IsRunning = true,
                                        ConnID = null,
                                        ConforenceNumber = null,
                                        isOnCall = false,
                                        isConforence = false,
                                        CurrentStatusID = 1,
                                        DialAccess = Convert.ToString(agentvalue.DialAccess),
                                        CampaignPhone = null,
                                        MasterPhone = null,
                                        MyCode = 0,
                                        CampaignMode = "",
                                        HoldMusic_Path = agentvalue.HoldMusic_Path,
                                        isbreak = false,
                                        isMarge = false,
                                        CampaignName=null,
                                        ocsApplicationID=null,
                                        requestID=1,
                                        Prifix= agentvalue.Prefix
                                    };
                                    agentSessions[agentId] = agentSession;
                                    Task.Run(() => ReceiveLoop(agentSession));
                                   
                                    return true;
                                }
                                else
                                {
                                    return false;
                                }

                            }
                            else
                            {
                                return false;
                            }

                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }                   
                }
            }
            catch (Exception ex)
            {
                errorMessage = $" CTI login failed: {ex.Message}";
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

            }
        }
        private static async Task ReceiveLoop(AgentSession session)
        {

            ConnectionId connID = null;
            Dictionary<string, string> attachedData = null;
            while (session.IsRunning)
            {
                try
                {
                    if (session.TServerProtocol.State != ChannelState.Opened)
                    {
                        await Task.Delay(200);
                        continue;
                    }
                    var message = session.TServerProtocol.Receive();
                    if (message != null)
                    {
                        LogToFile(message.Name);
                        string log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 🔔 Received: {message.GetType().Name}";
                        var status = HandleCtiEvent(message, session, ref connID, out attachedData, _hubContext);
                        if (attachedData != null)
                        {
                            await _hubContext.Clients.Group(session.AgentId).SendAsync("ReceiveAttachedData", attachedData);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogToFile($" ReceiveLoop Error: {ex.Message}");
                }
            }
        }


        private static string HandleCtiEvent(IMessage msg, AgentSession session, ref ConnectionId connID, out Dictionary<string, string> attachedData, IHubContext<CtiHub> hubContext)
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
                            session.ConnID = eventDialing.ConnID;
                            AgentStatusMapper.UpdateAgentStatus(2, session, hubContext);
                        }
                        break;

                    case EventNetworkReached.MessageName:
                        var eventNetworkReached = msg as EventNetworkReached;
                        if (eventNetworkReached != null && eventNetworkReached.ThisDN == session.DN)
                        {
                            if (session.ConnID == null && session.IVRConnID == null)
                            {
                                session.ConnID = eventNetworkReached.ConnID;
                            }

                            AgentStatusMapper.UpdateAgentStatus(2, session, hubContext);
                        }
                        break;

                    case EventRinging.MessageName:
                        var ringing = msg as EventRinging;
                        if (ringing != null)
                        {
                            AgentStatusMapper.UpdateAgentStatus(2, session, hubContext);
                        }
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
                            session.CampaignPhone = established.ANI;
                            session.isOnCall = true;
                            AgentStatusMapper.UpdateAgentStatus(3, session, hubContext);
                        }
                        else
                        {
                            session.CampaignPhone = established.DNIS;
                            session.isOnCall = true;
                            AgentStatusMapper.UpdateAgentStatus(3, session, hubContext);
                        }
                        break;

                    case EventReleased.MessageName:
                        var released = msg as EventReleased;
                        if (released.ThisDN == session.DN)
                        {
                            var Tserver = session.TServerProtocol;
                            if (session.IVRConnID != null && released.ANI != null)
                            {
                                RequestRetrieveCall retrievecall = RequestRetrieveCall.Create(session.DN, session.ConnID);
                                var iMessage = Tserver.Request(retrievecall);
                                session.IVRConnID = null;
                                session.isConforence = false;
                                AgentStatusMapper.UpdateAgentStatus(3, session, hubContext);
                            }
                            else
                            {
                                if (session.IVRConnID != null)
                                {
                                    RequestReleaseCall releasecall = RequestReleaseCall.Create(session.DN, session.IVRConnID);
                                    var iMessage = Tserver.Request(releasecall);
                                    session.IVRConnID = null;
                                    session.isConforence = false;
                                    session.isMarge = false;
                                    RequestRetrieveCall retrievecall = RequestRetrieveCall.Create(session.DN, session.ConnID);
                                    var iMessage1 = Tserver.Request(retrievecall);
                                    if (iMessage1.Name == "EventError")
                                    {
                                        AgentStatusMapper.UpdateAgentStatus(4, session, hubContext);
                                    }
                                    else
                                    {
                                        session.isConforence = false;
                                        session.isOnCall = true;
                                        AgentStatusMapper.UpdateAgentStatus(3, session, hubContext);
                                    }

                                }
                                else
                                {
                                    session.isOnCall = false;
                                    session.isConforence = false;
                                    session.ConnID = null;
                                    AgentStatusMapper.UpdateAgentStatus(4, session, hubContext);
                                }
                            }
                        }

                        break;
                    case EventAgentReady.MessageName:
                        try
                        {
                            EventAgentReady eventagentready = msg as EventAgentReady;
                            if (eventagentready.ThisDN == session.DN)
                            {
                                session.isbreak = false;
                                AgentStatusMapper.UpdateAgentStatus(1, session, hubContext);
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
                            AgentStatusMapper.UpdateAgentStatus(1, session, hubContext);
                        }
                        break;
                    case EventAgentLogout.MessageName:
                        EventAgentLogout eventagentlogout = msg as EventAgentLogout;
                        if (eventagentlogout.ThisDN == session.DN)
                        {
                        }
                        break;
                    case EventDNOutOfService.MessageName:
                        EventDNOutOfService eventdNOutOfservice = msg as EventDNOutOfService;
                        if (eventdNOutOfservice.ThisDN == session.DN)
                        {

                        }
                        break;
                    case EventDNBackInService.MessageName:
                        EventDNBackInService eventdnbackinservice = msg as EventDNBackInService;
                        if (eventdnbackinservice.ThisDN == session.DN)
                        {

                        }
                        break;
                    case EventDestinationBusy.MessageName:
                        EventDestinationBusy eventdestinationbusy = msg as EventDestinationBusy;
                        if (eventdestinationbusy.ThisDN == session.DN)
                        {
                            var tserver = session.TServerProtocol;

                            if (session.IVRConnID != null)
                            {
                                var release = RequestReleaseCall.Create(session.DN, session.IVRConnID);
                                var massage = tserver.Request(release);

                                RequestRetrieveCall requestRetrieveCall = RequestRetrieveCall.Create(session.DN, session.ConnID);
                                var IMassage = tserver.Request(requestRetrieveCall);

                                session.IVRConnID = null;
                                session.isConforence = false;
                                AgentStatusMapper.UpdateAgentStatus(3, session, hubContext);
                            }
                            else
                            {
                                AgentStatusMapper.UpdateAgentStatus(4, session, hubContext);
                            }
                        }
                        break;
                    case EventPartyChanged.MessageName:
                        EventPartyChanged eventPartyChanged = msg as EventPartyChanged;
                        if (eventPartyChanged.ThisDN == session.DN)
                        {
                            session.CampaignPhone = eventPartyChanged.ANI;
                            session.ConnID = eventPartyChanged.ConnID;
                            AgentStatusMapper.UpdateAgentStatus(4, session, hubContext);
                        }
                        break;
                    case EventPartyDeleted.MessageName:
                        try
                        {
                            EventPartyDeleted eventPartydeleted = msg as EventPartyDeleted;
                            if (eventPartydeleted.ThisDN.ToString().Equals(session.DN))
                            {
                                session.isConforence = false;
                                session.IVRConnID = null;
                                session.isMarge = false;
                                AgentStatusMapper.UpdateAgentStatus(3, session, hubContext);
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
                                    AgentStatusMapper.UpdateAgentStatus(3, session, hubContext);
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
                            var iMessage = Tserver.Request(requestAgentNotReady);

                            AgentStatusMapper.UpdateAgentStatus(4, session, hubContext);
                        }
                        break;
                    case EventAbandoned.MessageName:
                        EventAbandoned eventAbandoned = msg as EventAbandoned;
                        if (eventAbandoned.ThisDN == session.DN)
                        {
                            session.ConnID = eventAbandoned.ConnID;
                            session.CampaignPhone = eventAbandoned.ANI;
                            AgentStatusMapper.UpdateAgentStatus(4, session, hubContext);
                        }
                        break;
                    case EventAttachedDataChanged.MessageName:
                        EventAttachedDataChanged eventattacheddatachanged = msg as EventAttachedDataChanged;
                        if (eventattacheddatachanged.ThisDN == session.DN)
                        {
                            AgentStatusMapper.UpdateAgentStatus(3, session, hubContext);
                            for (int i = 0; i < eventattacheddatachanged.UserData.Count; i++)
                            {
                                if (eventattacheddatachanged.UserData.Keys[i] == "GSW_PHONE")
                                {
                                    session.CampaignPhone = eventattacheddatachanged.UserData[i].ToString();
                                    session.MasterPhone = eventattacheddatachanged.UserData[i].ToString();
                                }
                            }

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
                            if (eventUserEvent.UserData.GetAsString("GSW_USER_EVENT") != null)
                            {
                                string ss = eventUserEvent.UserData["GSW_USER_EVENT"].ToString();
                                if (eventUserEvent.UserData["GSW_USER_EVENT"].ToString() == "CampaignStarted")
                                {
                                    session.ocsApplicationID = Convert.ToInt16(eventUserEvent.UserData["GSW_APPLICATION_ID"].ToString());
                                    session.CampaignName = Convert.ToString(eventUserEvent.UserData["GSW_CAMPAIGN_NAME"].ToString());
                                    session.CampaignMode = Convert.ToString(eventUserEvent.UserData["GSW_CAMPAIGN_MODE"].ToString());

                                    if (eventUserEvent.UserData["GSW_CAMPAIGN_MODE"].ToString() == "Preview")
                                    {

                                    }
                                    else
                                    {


                                    }
                                    if (session.CampaignName == null)
                                    {

                                    }
                                }
                                else if (eventUserEvent.UserData["GSW_USER_EVENT"].ToString() == "CampaignStopped")
                                {

                                }
                                else if (eventUserEvent.UserData["GSW_USER_EVENT"].ToString() == "CampaignLoaded")
                                {

                                }
                                else if (eventUserEvent.UserData["GSW_USER_EVENT"].ToString() == "CampaignUnloaded")
                                {

                                }
                     
                            }
                            if (eventUserEvent.UserData.GetAsString("GSW_USER_EVENT") != null)
                            {
                                if (eventUserEvent.UserData["GSW_USER_EVENT"].ToString() == "PreviewRecord")
                                {
                                    session.MyCode = Convert.ToDouble(eventUserEvent.UserData["TMasterID"].ToString());
                                    if (eventUserEvent.UserData.ContainsKey("GSW_CAMPAIGN_MODE") && eventUserEvent.UserData["GSW_CAMPAIGN_MODE"] != null)
                                    {
                                        session.CampaignMode = eventUserEvent.UserData["GSW_CAMPAIGN_MODE"].ToString();
                                    }
                                    else
                                    {

                                    }

                                    if (session.MyCode > 0)
                                    {
                                        session.CampaignPhone = Convert.ToString(eventUserEvent.UserData["GSW_PHONE"].ToString());
                                        session.MasterPhone = Convert.ToString(eventUserEvent.UserData["GSW_PHONE"].ToString());
                                        CTIConnectionManager.autoDial( session.AgentId, session.CampaignPhone , session.DN , session.TServerProtocol , session.Prifix);
                                    }
                                }
                                if (eventUserEvent.UserData["GSW_USER_EVENT"].ToString() == "ScheduledCall")
                                {
                                    
                                        session.MyCode = Convert.ToDouble(eventUserEvent.UserData["TMasterID"].ToString());
                                        if (session.MyCode > 0)
                                        {
                                            session.CampaignPhone = Convert.ToString(eventUserEvent.UserData["GSW_PHONE"].ToString());
                                            session.MasterPhone = Convert.ToString( eventUserEvent.UserData["GSW_PHONE"].ToString());
                                            if (session.CampaignMode != "Preview")
                                            {
                                            }
                                        }
                                    
                                }
                                if (eventUserEvent.UserData.GetAsString("GSW_ERROR") != null)
                                {
                                    if (eventUserEvent.UserData["GSW_ERROR"].ToString() == "No Records Available")
                                    {
                                       
                                    }
                                    else
                                    {
                                       
                                    }
                                }

                                }
                        }
                        break;

                    default:

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
            throw new Exception("No session found for the agent.");
        }
        public static void autoDial(string agentid,string number , string dn , TServerProtocol tserver , string prifix)
        {

            CTIConnectionManager.MakeCall(dn, agentid, number);

        }
        public static async Task MakeCall(string exten, string agentId, string phoneNumber)
        {
            string txph = "";

            if (!agentConnections.ContainsKey(agentId))
                throw new Exception(" Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            if (session.DialAccess == "0")
            {

                //await Broadcast("You don't have the dial access", session.AgentId);
            }
            else
            {
                if (session.isOnCall == false)
                {
                    if (phoneNumber.Contains('X'))
                    {
                        txph = session.CampaignPhone;
                    }
                    else
                    {
                        txph = phoneNumber;
                    }
                    if (txph == "")
                    {
                        txph = session.MasterPhone;
                    }
                    if (txph == "")
                    {
                        await Broadcast("Not allowed to dial without Number", session.AgentId);
                        return;
                    }
                    if (session.CurrentStatusID == 4)
                    {
                        KeyValueCollection reasonCodes = new KeyValueCollection();
                        reasonCodes.Add("ReasonCode", "ManualDialing");
                        RequestAgentNotReady requestAgentNotReady = RequestAgentNotReady.Create(session.DialAccess, AgentWorkMode.AuxWork, null, reasonCodes, reasonCodes);
                        var iMessage = tServer.Request(requestAgentNotReady);
                        if (iMessage.Name == "EventError")
                        {
                            await Broadcast(iMessage.Name, session.AgentId);
                        }
                    }
                    string DialPhone = "";
                    if (session.CampaignPhone == "")
                    {

                        DialPhone = phoneNumber;
                        session.CampaignPhone = phoneNumber;
                    }
                    else if (session.CampaignPhone == txph || txph.Contains("X"))
                    {

                        DialPhone = session.CampaignPhone;
                    }
                    else
                    {
                        DialPhone = phoneNumber;
                    }

                    if (DialPhone != "" && DialPhone.Length > 9)
                    {
                        if (DialPhone.Length > 14)
                        {
                            {
                                RequestMakeCall requestMakeCall = RequestMakeCall.Create(session.DN, DialPhone, MakeCallType.Regular);
                                var iMessage = tServer.Request(requestMakeCall);
                                if (iMessage.Name == "EventError")
                                {

                                }
                                else
                                {
                                    session.isOnCall = true;
                                    AgentStatusMapper.UpdateAgentStatus(2, session, CTIConnectionManager.HubContext);
                                }
                            }
                        }
                        else if (DialPhone.Length == 14)
                        {

                            RequestMakeCall requestMakeCall = RequestMakeCall.Create(session.DN, DialPhone, MakeCallType.Regular);
                            var iMessage = tServer.Request(requestMakeCall);
                            if (iMessage.Name == "EventError")
                            {
                            }
                            else
                            {
                                session.isOnCall = true;
                                AgentStatusMapper.UpdateAgentStatus(2, session, CTIConnectionManager.HubContext);
                            }

                        }
                        else if (DialPhone.Length >= 10)
                        {
                            RequestMakeCall requestMakeCall = RequestMakeCall.Create(session.DN, DialPhone, MakeCallType.Regular);
                            var iMessage = tServer.Request(requestMakeCall);
                            if (iMessage.Name == "EventError")
                            {
                            }
                            else
                            {
                                session.isOnCall = true;
                                AgentStatusMapper.UpdateAgentStatus(2, session, CTIConnectionManager.HubContext);
                            }
                        }
                        else
                        {
                            await Broadcast("Enter Proper Phone Number..", session.AgentId);
                        }
                    }
                }
                else
                {
                    //await Broadcast("Agent Is Already On Call", session.AgentId);
                }
            }
        }
        public static async Task Hold(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception("Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            if (session.isOnCall == true)
            {
                RequestHoldCall requestHoldCall = RequestHoldCall.Create(session.DN, session.ConnID);
                var IMassage = tServer.Request(requestHoldCall);
                if (IMassage.Name == "EventError")
                {
                    await Broadcast("EventError While Hold", session.AgentId);
                }
                else
                {

                    AgentStatusMapper.UpdateAgentStatus(10, session, _hubContext);

                }
            }
            else
            {

            }

        }
        public static async Task Unhold(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception(" Agent not logged in.");

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

                    AgentStatusMapper.UpdateAgentStatus(3, session, _hubContext);

                }
            }
        }

        public static void Conference(string agentId, string Number)
        {

            if (!agentConnections.ContainsKey(agentId))
                throw new Exception(" Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            lock (session.LockObj)
            {
                session.ConforenceNumber = Number;
                if (session.isOnCall == true)
                {
                    if (session.isConforence == false)
                    {
                        String hold_music_path = session.HoldMusic_Path;
                        KeyValueCollection extensionData = new KeyValueCollection();
                        extensionData.Add("music", hold_music_path);
                        RequestHoldCall requestHoldCall = RequestHoldCall.Create(session.DN, session.ConnID, extensionData, extensionData);
                        var iMessage = tServer.Request(requestHoldCall);

                        RequestInitiateConference requestic = RequestInitiateConference.Create(session.DN, session.ConnID, Number);
                        iMessage = tServer.Request(requestic);

                        if (iMessage.Name == "EventError")
                        {
                            session.isConforence = false;

                        }
                        else
                        {
                            session.isConforence = true;
                        }
                        switch (iMessage.Name)
                        {
                            case EventDialing.MessageName:
                                var eventDialing = iMessage as EventDialing;
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
                throw new Exception(" Agent not logged in.");

            AgentSession session = GetAgentSession(agentId);

            if (session.ConnID == null || session.IVRConnID == null)
                throw new Exception(" One or both ConnIDs are missing. Cannot merge calls.");

            var tServer = session.TServerProtocol;
            if (session.isOnCall == true)
            {

                var requestCompleteConference = RequestCompleteConference.Create(
               session.DN,
               session.ConnID,
               session.IVRConnID);
               
                 var IMassage = tServer.Request(requestCompleteConference);
                if(IMassage.Name== "EventError")
                {
                    session.isMarge = false;

                }
                else
                {
                    session.isMarge = true;

                }

            }

        }


        public static async Task AgentBreak(string agentId, string brkstatus)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception(" Agent not logged in.");

            AgentSession session = GetAgentSession(agentId);
            var tServer = session.TServerProtocol;
            if (session.isbreak == true)
            {
                
            }
            else
            {
                int statusCode = Convert.ToInt32(brkstatus);
                string breakReason = AgentStatusMapper.StatusMap.ContainsKey(statusCode) ? AgentStatusMapper.StatusMap[statusCode] : "UNKNOWN";

                KeyValueCollection reasonCodes = new KeyValueCollection();
                reasonCodes.Add("ReasonCode", breakReason);
                RequestAgentNotReady requestAgentNotReady = RequestAgentNotReady.Create(session.DN, AgentWorkMode.AuxWork, null, reasonCodes, reasonCodes);
                var iMassage = tServer.Request(requestAgentNotReady);
                if (iMassage.Name == "EventError")
                {
                    session.isbreak = false;
                }
                else
                {
                    session.isbreak = true;
                }
                AgentStatusMapper.UpdateAgentStatus(Convert.ToInt32(brkstatus), session, CTIConnectionManager.HubContext);
            }
        }


        public static void transferCall(string agentId, string routePoint)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception(" Agent not logged in.");

            AgentSession session = GetAgentSession(agentId);

            var tServer = session.TServerProtocol;
            string RouteValue = Convert.ToString(routePoint);
            RequestSingleStepTransfer requestsinglesteptransfer = RequestSingleStepTransfer.Create(session.DN, session.ConnID, RouteValue);
           var IMassage = tServer.Request(requestsinglesteptransfer);
            if(IMassage.Name == "EventError")
            {
                AgentStatusMapper.UpdateAgentStatus(Convert.ToInt32(3), session, CTIConnectionManager.HubContext);
            }
            else
            {
                AgentStatusMapper.UpdateAgentStatus(Convert.ToInt32(4), session, CTIConnectionManager.HubContext);
            }
        }

        public static void PartyDelete(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception(" Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            if (session.ConnID == null)
                throw new Exception(" No active agent call found.");

            if (session.IVRConnID == null)
                throw new Exception(" No IVR leg found to release.");
            if (session.IVRConnID != null)
            {
                if (session.isConforence == true)
                {
                    if(session.isMarge==true)
                    {
                        RequestDeleteFromConference releaseIVR = RequestDeleteFromConference.Create(session.DN, session.ConnID, session.ConforenceNumber);
                        var IMassage = tServer.Request(releaseIVR);
                        if (IMassage.Name == "EventError")
                        {
                        }
                        else
                        {
                            session.isMarge = false;
                            session.IVRConnID = null;
                            session.ConforenceNumber = null;
                            session.isConforence = false;
                            AgentStatusMapper.UpdateAgentStatus(Convert.ToInt32(3), session, CTIConnectionManager.HubContext);
                        }
                    }
                    else
                    {
                        var releaseCall = RequestReleaseCall.Create(session.DN, session.IVRConnID);
                        var IMassage2 = tServer.Request(releaseCall);
                        RequestRetrieveCall retrievecall = RequestRetrieveCall.Create(session.DN, session.ConnID);
                        var iMessage = tServer.Request(retrievecall);
                       
                        if (iMessage.Name == "EventError")
                        {
                        }
                        else
                        {  
                            session.isMarge = false;
                            session.IVRConnID = null;
                            session.ConforenceNumber = null;
                            session.isConforence = false;
                            AgentStatusMapper.UpdateAgentStatus(Convert.ToInt32(3), session, CTIConnectionManager.HubContext);
                        }
                    }
                }
            }
        }
        public static void Disconnect(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception(" Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);

            //if (session.IVRConnID == null && session.isConforence == false)
            //{
                if (session.ConnID != null)
                {
                    var releaseCall = RequestReleaseCall.Create(session.DN, session.ConnID);
                    var Imassage = tServer.Request(releaseCall);
                    if (Imassage.Name == "EventError")
                    {

                    }
                    else
                    {
                        session.ConnID = null;
                        session.IVRConnID = null;
                        session.isConforence = false;
                        session.isOnCall = false;
                        session.isMarge = false;
                    }
                //}
            }
        }

        public static void AgentReady(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception(" Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            RequestAgentReady requestAgentReady = RequestAgentReady.Create(session.DN, AgentWorkMode.AutoIn);
            var Imassage = tServer.Request(requestAgentReady);
            if (Imassage.Name == "EventError")
            {

            }
            else
            {
                session.isbreak = false;
                session.ConnID = null;
                session.IVRConnID = null;
                session.isConforence = false;
                session.isMarge = false;
                session.isOnCall = false;
                session.CurrentStatusID = 1;
                


                AgentStatusMapper.UpdateAgentStatus(Convert.ToInt32(1), session, CTIConnectionManager.HubContext);
            }
        }

        public static void GetNextCall(string agentId)
        {
            if (!agentConnections.ContainsKey(agentId))
                throw new Exception(" Agent not logged in.");

            var tServer = agentConnections[agentId];
            AgentSession session = GetAgentSession(agentId);
            try
            {
                if (session.CampaignMode == "Preview" && session.CampaignName != null)
                {
                    KeyValueCollection kvp = new KeyValueCollection();
                    kvp.Add("GSW_AGENT_REQ_TYPE", "PreviewRecordRequest");
                    kvp.Add("GSW_APPLICATION_ID", session.ocsApplicationID);
                    kvp.Add("GSW_CAMPAIGN_NAME", session.CampaignName);
                    CommonProperties commonProperties = CommonProperties.Create();
                    commonProperties.UserData = kvp;
                    RequestDistributeUserEvent requestDistributeUserEvent1 = RequestDistributeUserEvent.Create(session.DN, commonProperties);
                    int id = session.requestID + 1;
                    requestDistributeUserEvent1.ReferenceID = id;
                    var iMessage = tServer.Request(requestDistributeUserEvent1);  
                }
            }
            catch
            {
                return;
            }

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
