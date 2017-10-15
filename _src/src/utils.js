export default {
    getLanguage() {
        // Chrome and Firefox 32+
        return navigator.languages || [
          navigator.language ||
          navigator.browserLanguage
        ]
    }
}
