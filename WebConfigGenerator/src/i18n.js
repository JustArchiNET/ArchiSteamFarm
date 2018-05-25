const defaultLocale = 'strings';
const nameRegex = /\.\/(\S+)\.json/i;

function getLocale(validLocales) {
    const language = navigator.language; // If the browser doesn't support this, it will not support other page elements as well
    if (!language) return defaultLocale; // If the browser doesn't provide the language - return default locale
    if (language.length !== 2) return validLocales.includes(language) ? language : defaultLocale; // If the language is in `xx-XX` format, check if it's valid
    if (validLocales.includes(`${language}-${language.toUpperCase()}`)) return `${language}-${language.toUpperCase()}`; // If the language is two letter code, check if corresponding 5 letter code is a valid locale

    const languageRegex = new RegExp(`${language}\-\\\S\\\S`); // Create a regex to match `xx-**` where `*` is a wildcard

    for (const validLocale of validLocales) {
        if (languageRegex.test(validLocale)) return validLocale; // Check if the locale matches the regex, if so, return it
    }

    return defaultLocale; // If no match found, return default locale
}

function loadLocales() {
    const locales = {};
    const defaultLanguageFile = `./${defaultLocale}.json`;
    const languages = require.context('./locale/', false, /\.json/);

    locales[defaultLocale] = languages(defaultLanguageFile);

    for (const lang of languages.keys()) {
        if (lang === defaultLanguageFile) continue; // Already loaded.

        const languageName = lang.match(nameRegex)[1];
        const language = languages(lang);

        for (const key in language) {
            if (!language.hasOwnProperty(key)) continue;
            if (language[key] === '') language[key] = locales[defaultLocale][key];
        }

        locales[languageName] = language;
    }

    return locales;
}

const messages = loadLocales();
const locale = getLocale(Object.keys(messages));

export default { messages, locale };
