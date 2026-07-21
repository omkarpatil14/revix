pipeline {
    agent any

    environment {
        IMAGE_NAME = "omkarpatil13/revix-api"
        IMAGE_TAG = "${BUILD_NUMBER}"
    }

    stages {

        stage('Checkout') {
            steps {
                checkout scm
            }
        }

        stage('Restore') {
            steps {
                sh 'dotnet restore revix.sln'
            }
        }

        stage('Build') {
            steps {
                sh 'dotnet build revix.sln --configuration Release --no-restore'
            }
        }

        stage('Test') {
            steps {
                sh 'dotnet test revix.sln --no-build --configuration Release'
            }
        }

        stage('Build Docker Image') {
            steps {
                sh """
                    docker build \
                    -t ${IMAGE_NAME}:latest \
                    -t ${IMAGE_NAME}:${IMAGE_TAG} .
                """
            }
        }

        stage('Push Docker Image') {
            steps {
                withCredentials([
                    usernamePassword(
                        credentialsId: 'dockerhub',
                        usernameVariable: 'DOCKER_USERNAME',
                        passwordVariable: 'DOCKER_PASSWORD'
                    )
                ]) {

                    sh '''
                    echo "$DOCKER_PASSWORD" | docker login -u "$DOCKER_USERNAME" --password-stdin

                    docker push '"${IMAGE_NAME}"':latest
                    docker push '"${IMAGE_NAME}"':'"${IMAGE_TAG}"'

                    docker logout
                    '''
                }
            }
        }
    }

    post {
        success {
            echo "Pipeline completed successfully!"
        }

        failure {
            echo "Pipeline failed!"
        }
    }
}