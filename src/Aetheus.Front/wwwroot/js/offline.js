let language;
try {
    language = localStorage.getItem('aetheus_lang') || navigator.language;
} catch {
    language = navigator.language;
}
const offlineLanguage = /^fr/i.test(language || '') ? 'fr' : 'en';
document.documentElement.lang = offlineLanguage;
for (const element of document.querySelectorAll('[data-offline-lang]')) {
    element.hidden = element.dataset.offlineLang !== offlineLanguage;
}
