import Vue from 'vue';
import VueI18n from 'vue-i18n';

import App from './App.vue';
import i18nSettings from './i18n.js';
import router from './router.js';

Vue.use(VueI18n);
const i18n = new VueI18n(i18nSettings);

new Vue({
    el: '#app',
    router,
    i18n,
    template: '<App/>',
    components: { App }
});
