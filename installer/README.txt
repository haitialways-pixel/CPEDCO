CPCREDO — installation USB
Caisse Populaire Épargne et de Crédit pour le Développement de l’Ouest
Pétion-Ville, Haïti

SERVEUR (un seul PC)
1. Clic droit sur INSTALLER-SERVEUR.bat → Exécuter en tant qu’administrateur
2. Mot de passe d’installation (5 essais)
3. Dossier (défaut : C:\CPCREDO)
4. Mot de passe PostgreSQL (utilisateur postgres)
5. Notez le mot de passe admin affiché UNE FOIS
6. Changez-le à la première connexion : https://IP-DU-SERVEUR:5443
   La première visite du navigateur peut afficher un avertissement (certificat auto-signé). Continuer vers le site.

CLIENT (caissiers, autres PC)
1. Double-cliquer INSTALLER-CLIENT.bat
2. Le même mot de passe d’installation
3. Adresse IP du serveur (sans http, sans port)
4. Un raccourci CPCREDO est créé sur le Bureau et le navigateur s’ouvre sur https://IP:5443
   La première visite peut afficher un avertissement (certificat auto-signé). Continuer vers le site.

Le mot de passe d’installation n’est pas écrit dans ce fichier.
Pas de fondateurs ni de données de démonstration en production.
Sauvegarde automatique : tous les jours a 18:30 (tache CPCREDO-Backup). Dossier habituel : C:\CPCREDO\backups.
