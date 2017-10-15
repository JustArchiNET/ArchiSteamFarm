// The Vue build version to load with the `import` command
// (runtime-only or standalone) has been set in webpack.base.conf with an alias.
import Vue from 'vue'
import VueI18n from 'vue-i18n'
import App from './App'
import router from './router'
import utils from './utils.js'

Vue.config.productionTip = false

Vue.use(VueI18n);

// i18n work
const supportLanguage = [ "zh-cn", "en" ];

const messages = {};

let locale = "en";

for (const lang of utils.getLanguage()) {
  if (supportLanguage.indexOf(lang.toLowerCase()) != -1) {
    locale = lang.toLowerCase();
    // First match
    break;
  }
}

messages[locale] = require(`./locale/${locale}`)

const i18n = new VueI18n({
    locale,
    messages,
})

/* eslint-disable no-new */
new Vue({
  el: '#app',
  router,
  i18n,
  template: '<App/>',
  components: { App }
})

if('serviceWorker' in navigator) {
    navigator.serviceWorker.register('service-worker.js');
}
