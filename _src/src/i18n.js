const defaultLocale = "en";

export default {
    getLanguage() {
        // Chrome and Firefox 32+
        return navigator.languages || [
          navigator.language ||
          navigator.browserLanguage
        ]
    },
    loadLocale(locale) {
        return require(`./locale/${locale}`);
    },
    getSplitedLanguage(language) {
        if (language.indexOf("-") != -1) {
            const splitedLang = language.substring(0, language.indexOf("-"))
            return splitedLang.toLowerCase();
        }
        return language
    },
    getLocale() {
        const messages = {};

        let locale = defaultLocale;

        for (const lang of this.getLanguage()) {
            try {
                locale = lang.toLowerCase();
                messages[locale] = this.loadLocale(locale);
                // First match
                break;
            // If import error will throw error
            } catch (e) {
                locale = defaultLocale; 
            }
        }

        // match head language when first match is failure
        if (locale === defaultLocale) {
            for (const lang of this.getLanguage()) {
            const splitedLang = this.getSplitedLanguage(lang);
                try {
                    locale = splitedLang;
                    messages[locale] = this.loadLocale(locale);
                    break;
                } catch (e) {
                    locale = defaultLocale;
                }
            }
        }
        messages[defaultLocale] = this.loadLocale(defaultLocale);
       
        return { messages, locale };
    }
}
