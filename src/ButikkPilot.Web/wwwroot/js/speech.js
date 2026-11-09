// Browser speech helpers. Everything here must fail soft: unsupported browsers just get "unavailable".
// Privacy note: in Chrome/Edge, SpeechRecognition streams audio to the browser vendor's servers.

let activeRecognition = null;

export function isRecognitionAvailable() {
  return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
}

export function isSynthesisAvailable() {
  return !!window.speechSynthesis;
}

// Resolves with the transcript, or null on error / no speech / timeout. Never stays pending.
export function listenOnce(lang) {
  return new Promise((resolve) => {
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Recognition) {
      resolve(null);
      return;
    }

    stopListening();

    let settled = false;
    let transcript = null;
    const settle = (value) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      activeRecognition = null;
      resolve(value);
    };

    let recognition;
    try {
      recognition = new Recognition();
    } catch {
      resolve(null);
      return;
    }

    activeRecognition = recognition;
    recognition.lang = lang || "nb-NO";
    recognition.interimResults = false;
    recognition.maxAlternatives = 1;
    recognition.onresult = (event) => {
      transcript = event.results?.[0]?.[0]?.transcript ?? null;
    };
    recognition.onerror = () => settle(null);
    recognition.onnomatch = () => settle(null);
    recognition.onend = () => settle(transcript);

    const timer = setTimeout(() => {
      try { recognition.stop(); } catch { /* already stopped */ }
      settle(transcript);
    }, 12000);

    try {
      recognition.start();
    } catch {
      settle(null);
    }
  });
}

export function stopListening() {
  try { activeRecognition?.stop(); } catch { /* ignore */ }
  activeRecognition = null;
}

export function speak(text, lang) {
  if (!text || !window.speechSynthesis) return;
  const utterance = new SpeechSynthesisUtterance(text);
  utterance.lang = lang || "nb-NO";
  utterance.rate = 1;
  window.speechSynthesis.cancel();
  window.speechSynthesis.speak(utterance);
}

export function stopSpeaking() {
  try { window.speechSynthesis?.cancel(); } catch { /* ignore */ }
}
