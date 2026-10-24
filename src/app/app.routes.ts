import { Routes } from '@angular/router';

export const routes: Routes = [
	{
		path: 'login',
		loadComponent: () => import('./login/login').then((module) => module.Login),
	},
	{
		path: 'register',
		loadComponent: () => import('./register/register').then((module) => module.Register),
	},
	{
		path: 'home',
		loadComponent: () => import('./home/home').then((module) => module.Home),
	},
	{ path: '', pathMatch: 'full', redirectTo: 'home' },
	{ path: '**', redirectTo: 'home' },
];
