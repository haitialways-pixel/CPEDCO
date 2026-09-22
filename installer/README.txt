CPCREDO — installation USB
Caisse Populaire Épargne et de Crédit pour le Développement de l’Ouest
Pétion-Ville, Haïti

SERVEUR (un seul PC)
1. Clic droit sur Installer-CPCREDO.exe → Exécuter en tant qu’administrateur
   (ou INSTALLER-SERVEUR.bat si le .exe n’est pas présent)
2. Mot de passe d’installation
3. Suivez les écrans (Suivant). Le dossier est C:\CPCREDO.
4. Notez le mot de passe admin affiché UNE FOIS, puis cliquez sur Démarrer
5. Changez-le à la première connexion : https://IP-DU-SERVEUR:5443
   La première visite du navigateur peut afficher un avertissement (certificat auto-signé). Continuer vers le site.
   Si .NET ou PostgreSQL manquent, l’installeur les pose depuis OfflinePackages (sans internet).

CLIENT (caissiers, autres PC)
1. Double-cliquer INSTALLER-CLIENT.bat
2. Le même mot de passe d’installation
3. Adresse IP du serveur (sans http, sans port)
4. Un raccourci CPCREDO est créé sur le Bureau et le navigateur s’ouvre sur https://IP:5443
   La première visite peut afficher un avertissement (certificat auto-signé). Continuer vers le site.

Le mot de passe d’installation n’est pas écrit dans ce fichier.
Pas de fondateurs ni de données de démonstration en production.
Sauvegarde automatique : tous les jours a 18:30 (tache CPCREDO-Backup). Dossier habituel : C:\CPCREDO\backups.
