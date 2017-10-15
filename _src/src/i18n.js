const supportLanguage = [ "zh-cn", "en" ];

export default {
    getLanguage() {
        // Chrome and Firefox 32+
        return navigator.languages || [
          navigator.language ||
          navigator.browserLanguage
        ]
    },
    getSplitedLanguage(language) {
        if (language.indexOf("-") != -1) {
            const splitedLang = lang.substring(0, lang.indexOf("-"))
            return splitedLang.toLowerCase();
        }
        return language
    },
    getLocale() {
        const supportLanguage = [ "zh-cn", "en" ];

        const messages = {};

        let locale = "en";

        for (const lang of this.getLanguage()) {
            if (supportLanguage.indexOf(lang.toLowerCase()) != -1) {
                locale = lang.toLowerCase();
                // First match
                break;
            }
        }

        // match head language when first match is failure
        if (locale === "en") {
            for (const lang of this.getLanguage()) {
            const splitedLang = this.getSplitedLanguage(lang);
                if (supportLanguage.indexOf(splitedLang) != -1) {
                    locale = splitedLang;
                    break;
                }
            }
        }

        messages[locale] = require(`./locale/${locale}`);
        return { messages, locale };
    }
}
