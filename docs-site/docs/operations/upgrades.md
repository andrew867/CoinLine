# Upgrades

1. **DB:** run new EF migrations before new API.
2. **API:** blue/green or rolling — stateless.
3. **UI:** deploy new static assets; clear CDN cache if used.
