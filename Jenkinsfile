pipeline {
    agent any

    options {
        timestamps()
    }

    stages {
         
         stage('Checkout'){
            steps {
                checkout scm
            }
         }

         stage('Restore'){
            steps {
                sh 'dotnet restore revix.sln'
            }
         }

         stage('Build'){
            steps {
                sh 'dotnet build revix.sln --configuration Release --no-restore'
            }
         }

         stage('Test'){
            steps {
                sh 'dotnet test revix.sln --configuration Release --no-build'
            }
         }
    }

    post {
        success {
            echo 'Build Completed Successfully!'
        }

        failure {
            echo 'Build Failed!'
        }

        always {
            cleanWs()
        }
    }
}