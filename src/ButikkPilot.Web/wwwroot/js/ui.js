export function scrollToBottom(element) {
  if (element) {
    element.scrollTop = element.scrollHeight;
  }
}

export function getBrowserLanguage() {
  return navigator.language || "nb-NO";
}

export function setDocumentLanguage(code) {
  document.documentElement.lang = (code || "nb-NO").split("-")[0];
}

export function reload() {
  location.reload();
}
