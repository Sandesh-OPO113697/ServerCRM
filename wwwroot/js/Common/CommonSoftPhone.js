
let userAgent = null;
let session = null;

async function initSoftphone(config) {

    console.log(JSON.stringify(config));

    if (userAgent) {
        console.log("JsSIP already initialized, skipping.");
        return;
    }

    try {

        let localStream = await navigator.mediaDevices.getUserMedia({ audio: true });
        const localAudio = document.getElementById('localAudio');
        localAudio.srcObject = localStream;

        let wsUri = "ws://" + config.sipserver + "/ws";
        const socket = new JsSIP.WebSocketInterface(wsUri);
        const configuration = {
            sockets: [socket],
            uri: "sip:" + config.username + "@@" + config.sipserver,
            password: config.password,
        };

        userAgent = new JsSIP.UA(configuration);
        userAgent.on('connected', () => console.log("Connected to SIP server"));
        userAgent.on('disconnected', () => console.log("Disconnected from SIP server"));
        userAgent.on('registered', () => console.log("Registered as", config.username));
        userAgent.on('unregistered', () => console.log("Unregistered"));
        userAgent.on('registrationFailed', e => console.error("Registration failed", e));


        userAgent.on('newRTCSession', e => {
            session = e.session;

            $('#btnAnswer').click(function () {

                if (session && session.direction === 'incoming') {
                    session.answer({
                        mediaConstraints: { audio: true, video: false }
                    });

                } else {
                    console.warn(" No incoming call to answer");
                }
            });

            if (session.direction === 'incoming') {
                document.getElementById("currentEvent").textContent = "Incoming call from " + session.remote_identity.uri.toString();
                session.on('peerconnection', e => {
                    const pc = e.peerconnection;
                    pc.ontrack = ev => {

                        if (ev.streams && ev.streams[0]) {
                            document.getElementById("remoteAudio").srcObject = ev.streams[0];
                        }
                    };
                });

                session.on('ended', () => console.log(" Call ended"));
                session.on('failed', e => console.error(" Call failed", e));
                session.on('confirmed', () => console.log(" Call answered"));
            }

            if (session.direction === 'outgoing') {
                if (localStream) {
                    localStream.getTracks().forEach(track => {
                        session.connection.addTrack(track, localStream);
                    });
                }
            }
        });
        userAgent.start();

        refreshFsStatus();





    } catch (err) {
        console.error("Softphone init failed", err);
    }
}

$(document).ready(function () {
    const sipConfig = JSON.parse(sessionStorage.getItem("sipConfig"));

    if (sipConfig) {
        console.log("Reusing SIP config from sessionStorage");
        initSoftphone(sipConfig);
    } else {
        $.get("/LogIn/GetSipConfig", function (res) {
            console.log("Loaded SIP config:", res);
            sessionStorage.setItem("sipConfig", JSON.stringify(res));
            initSoftphone(res);
        }).fail(err => console.error("Failed to fetch SIP config", err));
    }
});

async function refreshFsStatus() {
    const response = await fetch('/FreeswitchDialer/Status');
    const data = await response.json();

}