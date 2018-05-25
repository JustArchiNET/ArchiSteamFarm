import Vue from 'vue';
import Router from 'vue-router';

Vue.use(Router);

export default new Router({
    routes: [
        {
            path: '/',
            name: 'home',
            component: () => import('./components/Home.vue')
        },
        {
            path: '/asf',
            name: 'asf-config',
            component: () => import('./components/ASFConfig.vue')
        },
        {
            path: '/bot',
            name: 'bot-config',
            component: () => import('./components/BotConfig.vue')
        }
    ]
});
