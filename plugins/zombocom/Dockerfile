FROM nginx:1.27-alpine
COPY app/ /usr/share/nginx/html/
RUN sed -i 's/index  index.html/index  index.xhtml index.html/' /etc/nginx/conf.d/default.conf
EXPOSE 80
