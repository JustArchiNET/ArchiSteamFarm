export default {
    getLanguage() {
        if (navigator.language) {
            return navigator.language;
        } else {
            return navigator.browserLanguage;
        }
    }
}
