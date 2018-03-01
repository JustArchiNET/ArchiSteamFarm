import ASFConfig from '@/components/ASFConfig';
import BotConfig from '@/components/BotConfig';
import Home from '@/components/Home';

import Vue from 'vue';
import Router from 'vue-router';

Vue.use(Router);

export default new Router({
    routes: [
        {
            path: '/',
            name: 'Home',
            component: Home
        },
        {
            path: '/asf',
            name: 'ASFConfig',
            component: ASFConfig
        },
        {
            path: '/bot',
            name: 'BotConfig',
            component: BotConfig
        }
    ]
});
