const defaultLocale = 'strings';
var availableLanguages = [],
    tmpLanguage = get('language'),
    myLocal = (tmpLanguage) ? tmpLanguage : getLocale(availableLanguages);

function getLocale(validLocales) {
    const language = navigator.language || navigator.userLanguage; // If the browser doesn't support this, it will not support other page elements as well
    if (!language) return defaultLocale; // If the browser doesn't provide the language - return default locale
    if (language.length !== 2) return validLocales.includes(language) ? language : defaultLocale; // If the language is in `xx-XX` format, check if it's valid
    if (validLocales.includes(`${language}-${language.toUpperCase()}`)) return `${language}-${language.toUpperCase()}`; // If the language is two letter code, check if corresponding 5 letter code is a valid locale

    const languageRegex = new RegExp(`${language}\-\\\S\\\S`); // Create a regex to match `xx-**` where `*` is a wildcard

    for (const validLocale of validLocales) {
        if (languageRegex.test(validLocale)) return validLocale; // Check if the locale matches the regex, if so, return it
    }

    return defaultLocale; // If no match found, return default locale
}

function loadLocales(language) {
    var i18n = $.i18n(),
        langCode = (language === 'strings') ? 'us' : language.substr(language.length - 2).toLowerCase(),
        translationFile;

    i18n.locale = language;
    translationFile = '../locale/' + i18n.locale + '.json';
    i18n.load(translationFile, i18n.locale).done(
        function () {
            $.getJSON(translationFile, function (obj) {
                for (var prop in obj) {
                    if (obj.hasOwnProperty(prop)) {
                        if (obj[prop]) {
                            if (prop.substring(0, 12) === 'placeholder-') {
                                $('[data-i18n="' + prop + '"]').attr("placeholder", $.i18n(prop));
                            } else if (prop.substring(0, 6) === 'title-') {
                                $('[data-i18n="' + prop + '"]').attr("title", $.i18n(prop));
                            } else {
                                $('[data-i18n="' + prop + '"]').i18n();
                            }

                        }
                    }
                }
				
				// fix for bootstrap-select elements
				$('[data-id="commandsDropDown"] > .filter-option').text($.i18n('title-commands'));
				$('[data-id="botsDropDown"] > .filter-option').text($.i18n('title-bots'));
            });
        }
    );

    store('language', language);
    store('langCode', langCode);
}

function loadAllLanguages() {
    $.ajax({
        url: '/Api/WWW/Directory/locale',
        type: 'GET',
        async: false,
        success: function (data) {
            var obj = data.Result;
            const languageRegex = new RegExp("strings(.[a-z]{2}-[A-Z]{2})?.json");

            availableLanguages = [];

            for (var prop in obj) {
                if (obj.hasOwnProperty(prop)) {
                    var language = obj[prop];
                    if (languageRegex.test(language))
                        availableLanguages.push(language.substr(0, language.length - 5));
                }
            }
        }
    });
}

loadLocales(myLocal);