// The Vue build version to load with the `import` command
// (runtime-only or standalone) has been set in webpack.base.conf with an alias.
import Vue from 'vue';
import VueI18n from 'vue-i18n';

import App from './App.vue';
import appi18n from './i18n.js';
import router from './router';

Vue.config.productionTip = false;

Vue.use(VueI18n);

const { locale, messages } = appi18n.getLocale();

const i18n = new VueI18n({
    locale,
    messages
});

/* eslint-disable no-new */
new Vue({
    el: '#app',
    router,
    i18n,
    template: '<App/>',
    components: { App }
});

if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('service-worker.js');
}
