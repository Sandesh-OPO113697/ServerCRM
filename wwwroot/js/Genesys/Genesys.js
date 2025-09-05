
function openTab(evt, tabId) {
    document.querySelectorAll(".tab-content").forEach(el => el.style.display = "none");
    document.querySelectorAll(".nav-tab").forEach(el => el.classList.remove("active"));
    document.querySelectorAll(".nav-tab").forEach(el => el.classList.add("inactive"));
    document.getElementById(tabId).style.display = "block";
    evt.currentTarget.parentElement.classList.add("active");
    evt.currentTarget.parentElement.classList.remove("inactive");
}

let timerInterval = null;
let seconds = 0;

function startTimer() {
    stopTimer();
    seconds = 0;
    console.log("Starting call timer...");
    timerInterval = setInterval(() => {
        seconds++;
        document.getElementById("callTimer").innerText = formatTime(seconds);
    }, 1000);
}

function stopTimer() {
    if (timerInterval) {
        console.log("Stopping call timer...");
        clearInterval(timerInterval);
        timerInterval = null;
    }
    seconds = 0;
    document.getElementById("callTimer").innerText = "00:00:00";
}

function formatTime(sec) {
    const hours = Math.floor(sec / 3600).toString().padStart(2, '0');
    const minutes = Math.floor((sec % 3600) / 60).toString().padStart(2, '0');
    const seconds = (sec % 60).toString().padStart(2, '0');
    return `${hours}:${minutes}:${seconds}`;
}

function toggleAccordion() {
    const content = document.getElementById("dialer-details");
    const arrow = document.getElementById("arrowIcon");
    if (content.style.maxHeight && content.style.maxHeight !== "0px") {
        content.style.maxHeight = "0";
        arrow.style.transform = "rotate(0deg)";
    } else {
        content.style.maxHeight = content.scrollHeight + "px";
        arrow.style.transform = "rotate(180deg)";
    }
}

function toggleBreakDropdown() {
    const dd = document.getElementById("breakDropdown");
    dd.style.display = (dd.style.display === "block") ? "none" : "block";
}

function toggleScheduleDropdown() {
    const dd = document.getElementById("scheduleDropdown");
    dd.style.display = (dd.style.display === "none" || dd.style.display === "") ? "block" : "none";
}

document.addEventListener("DOMContentLoaded", function () {
    const urlParams = new URLSearchParams(window.location.search);
    const agentId = urlParams.get('empCode');

    if (!agentId) {
        console.error("Agent ID (empCode) is missing in the URL.");
        return;
    }

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/ctihub")
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.Information)
        .build();

    connection.onreconnecting((error) => {
        console.warn("SignalR reconnecting...", error);
    });

    connection.onreconnected((connectionId) => {
        console.info("✅ SignalR reconnected:", connectionId);
        connection.invoke("JoinGroup", agentId)
            .then(() => console.log("Rejoined group after reconnect"))
            .catch(err => console.error("Error rejoining group:", err));
    });

    connection.onclose((error) => {
        console.error("SignalR connection closed permanently:", error);
        const statusElem = document.getElementById("status");
        if (statusElem) statusElem.innerText = "Disconnected. Please refresh the page.";
    });

    connection.on("UpdatePhoneInput", function (number) {
        let last10 = number.slice(-10);
        document.getElementById("phoneInput").value = last10;
    });

    connection.on("UserName", function (number) {
        const parts = number.split(".");
        const initials = parts.map(p => p.charAt(0).toUpperCase()).join("");
        const email = number + "@1point1.in";

        const usernameEl = document.getElementById("username");
        if (usernameEl) usernameEl.innerText = number;

        document.querySelectorAll(".user-avatar, .profile-avatar").forEach(el => el.innerText = initials);
        document.querySelectorAll(".profile-email").forEach(el => el.innerText = email);
    });

    connection.on("ReceiveStatus", handleStatusUpdate);
    connection.on("ReceiveAttachedDataUserEvent", handleAttachedDataUserEvent);
    connection.on("ReceiveAttachedData", handleAttachedData);

    async function startConnection() {
        try {
            await connection.start();
            console.log("SignalR connected");
            await connection.invoke("JoinGroup", agentId);
            await updateStatus();
            startTimer();
        } catch (err) {
            console.error("SignalR connection error:", err);
            setTimeout(startConnection, 5000);
        }
    }
    startConnection();
});


function handleStatusUpdate(message) {
    const statusElem = document.getElementById("status");
    if (statusElem) statusElem.innerText = message;
    const msg = message.toLowerCase();

    $("#dialGroup, #hold, #unhold, #confo, #merge, #party").hide();
    $("#break, #getnext").addClass("disabled").css({ "pointer-events": "none", "opacity": "0.5" });

    if (msg.includes("waiting")) {
        $("#break").removeClass("disabled").css({ "pointer-events": "auto", "opacity": "1" });
        $("#dialGroup").css("display", "flex");
        $("#getnext").removeClass("disabled").css({ "pointer-events": "auto", "opacity": "1" });
    }

    if (msg.includes("talking") || msg.includes("established") || msg.includes("outbound call established") || msg.includes("inbound call established")) {
        $("#dialGroup").hide();
        $("#hold, #confo, #merge, #party").css("display", "flex");
        $("#getnext").addClass("disabled").css({ "pointer-events": "none", "opacity": "0.5" });
    }

    if (msg.includes("hold")) $("#unhold").css("display", "flex");

    if (msg.includes("wraping")) {
        $("#dialGroup").css("display", "flex");
        $("#hold, #unhold, #confo, #merge, #party").hide();
        $("#getnext").addClass("disabled").css({ "pointer-events": "none", "opacity": "0.5" });
        $("#break").removeClass("disabled").css({ "pointer-events": "auto", "opacity": "1" });
    }

    if (["talking", "wraping", "dialing", "waiting", "established", "outbound call established", "inbound call established"].some(s => msg.includes(s))) {
        stopTimer();
        startTimer();
    } else if (["call ended", "disconnected", "hangup", "idle"].some(s => msg.includes(s))) {
        stopTimer();
    }

    if (msg.includes("break")) {
        stopTimer();
        startTimer();
    }
}

function handleAttachedDataUserEvent(data) {
    document.getElementById("batchId").innerText = data.Batch_id || "-";
    document.getElementById("mode").innerText = data.InteractionSubtype || "-";
    document.getElementById("mycode").innerText = data.TMasterID || "-";
    document.getElementById("eventType").innerText = data.GSW_USER_EVENT || "-";
    document.getElementById("campaignName").innerText = data.GSW_CAMPAIGN_NAME || "-";
}

function handleAttachedData(data) {
    if (data && typeof data === "object") {
        document.getElementById("campaignName").textContent = data.RTargetObjectSelected || "-";
        document.getElementById("mode").textContent = data.RStrategyName || "-";
        document.getElementById("objectId").textContent = data.RTargetAgentSelected || "-";
    }
}


function callAPI(url, method = 'POST', data = {}) {
    fetch(url, {
        method,
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(data)
    }).then(async res => {
        if (!res.ok) {
            const errorText = await res.text();
            if (errorText === "Agent ready for next call." || errorText === "Agent successfully logged out") {
                Swal.fire({ icon: 'success', title: 'Success', text: errorText }).then(result => {
                    if (errorText.includes("logged out") && result.isConfirmed) location.href = "/Pages/login.html";
                });
            } else {
                throw new Error(errorText || "API call failed");
            }
        } else console.log("✅ API call successful.");
    }).catch(err => Swal.fire({ icon: 'warning', title: 'Oops...', text: err.message || "Something went wrong!" }));
}

function makeCall() {
    const phone = document.getElementById("phoneInput").value.trim();
    if (!/^\d{10}$/.test(phone)) { alert("Phone number must be exactly 10 digits."); return; }
    callAPI("/api/Genesys/makecall", "POST", { phone });
}

function openConferencePrompt() {
    Swal.fire({
        title: 'Enter conference number',
        input: 'text',
        inputPlaceholder: 'e.g. 7058053821',
        showCancelButton: true,
        confirmButtonText: 'Join',
        inputValidator: value => (!value || !/^\d{10}$/.test(value)) ? 'Enter valid 10-digit number!' : null
    }).then(result => {
        if (result.isConfirmed) callAPI('/api/Genesys/conference', 'POST', { number: result.value });
    });
}

function sendBreak(reasonCode) {
    if (!reasonCode) { Swal.fire({ icon: 'warning', title: 'No break reason selected' }); return; }
    callAPI('/api/Genesys/break', 'POST', { reasonCode });
}

function sendTransfer(selectElement) {
    const route = selectElement.value;
    if (!route) { Swal.fire({ icon: 'warning', title: 'No transfer route selected' }); selectElement.selectedIndex = 0; return; }
    callAPI('/api/Genesys/transfer', 'POST', { route });
    setTimeout(() => selectElement.selectedIndex = 0, 300);
}


const subDispositions = {
    1: [{ id: 101, name: "Not Available" }, { id: 102, name: "Client Requested" }, { id: 103, name: "Wrong Time" }],
    2: [{ id: 201, name: "Customer Hung Up" }, { id: 202, name: "Agent Hung Up" }, { id: 203, name: "Network Issue" }],
    3: [{ id: 301, name: "Customer Busy" }, { id: 302, name: "On Another Call" }, { id: 303, name: "Will Call Later" }]
};

function updateSubDisposition() {
    const dispoId = document.getElementById("disposition").value;
    const subDispoSelect = document.getElementById("subDisposition");
    subDispoSelect.innerHTML = '<option value="">-- Select Sub Disposition --</option>';
    if (subDispositions[dispoId]) subDispositions[dispoId].forEach(sub => {
        const option = document.createElement("option");
        option.value = sub.id;
        option.textContent = sub.name;
        subDispoSelect.appendChild(option);
    });
}

async function submitDisposition() {
    const dispoId = parseInt(document.getElementById("disposition").value);
    const subDispoId = parseInt(document.getElementById("subDisposition").value);
    if (!dispoId || !subDispoId) { alert("Select both disposition and sub-disposition."); return; }

    try {
        const response = await fetch('/api/Genesys/submit', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ dispositionId: dispoId, subDispositionId: subDispoId })
        });

        if (response.ok) {
            const result = await response.json();
            Swal.fire({ icon: 'success', title: 'Success', text: result.message });
        } else Swal.fire({ icon: 'error', title: 'Error', text: 'Failed to submit disposition.' });
    } catch (error) {
        Swal.fire({ icon: 'error', title: 'Exception', text: error?.message || 'An unexpected error occurred.' });
    }
}


async function updateStatus() {
    const urlParams = new URLSearchParams(window.location.search);
    const agentId = urlParams.get('empCode');
    if (!agentId) { console.error("No agent ID found in URL."); return; }

    try {
        await fetch('/api/Genesys/status', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ empCode: agentId })
        });
    } catch (err) { console.error("Failed to get agent status:", agentId); }
}
