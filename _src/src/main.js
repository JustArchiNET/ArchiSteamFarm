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
const supportLanguage = [ "zh-CN", "default" ];

const messages = {};
for (const item of supportLanguage) {
    messages[item] = require("./locale/" + item);
}

let locale = "default";

if (supportLanguage.indexOf(utils.getLanguage()) != -1) {
    locale = utils.getLanguage()
}

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
