const defaultLocale = 'strings';
const nameRegex = /\.\/(\S+)\.json/i;

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
            var missing = 0,
                totalSize = 0;

            $.getJSON(translationFile, function (obj) {
                for (var prop in obj) {
                    if (obj.hasOwnProperty(prop)) {
                        totalSize++;
                        if (obj[prop]) {
                            $('[data-i18n="' + prop + '"]').i18n();
                        } else {
                            missing++;
                        }
                    }
                }

                if (missing > 0) {
                    var percentage = missing * 100 / totalSize;
                    $('#languageInfo').html('<div class="alert alert-warning alert-dismissible">'
                        + '<button title="Never show again" type="button" class="close" data-dismiss="alert" aria-hidden="true">x</button>'
                        + percentage.toFixed(0) + '% of this language is not translated! Help us <a href="https://github.com/JustArchi/ArchiSteamFarm/wiki/Localization">here</a>.'
                        + '</div>');
                } else {
                    $('#languageInfo').text('');
                }

                $('#languages').collapse('hide');
            });
        }
    );

    store('language', language);
    $('#currentLanguage').attr({
        alt: langCode,
        src: '../img/flags/' + langCode + '.gif'
    });
}

var availableLanguages = [];

function loadAllLanguages() {
    $.ajax({
        url: '/Api/WWW/Directory/locale',
        type: 'GET',
        async: false,
        success: function (data) {
            var obj = data['Result'];
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